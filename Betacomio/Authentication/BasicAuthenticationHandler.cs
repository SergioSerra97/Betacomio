﻿using Azure.Core;
using Azure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text;
using ErrorLogLibrary.DBConnect;
using System.Configuration;
using ConfigurationManager = System.Configuration.ConfigurationManager;
using Microsoft.Data.SqlClient;
using DBConnectionLibrary;

namespace Betacomio.Authentication
{
    public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        Encryption encryption = new Encryption();
        public BasicAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock
            ) : base(options, logger, encoder, clock)
        { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            //Controllo che l'autenticazione c'è nell'header
            Response.Headers.Add("WWW-Authenticate", "Basic");

            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.Fail("Autorizzazione mancante"));
            }

            var authorizationHeader = Request.Headers["Authorization"].ToString();

            //Stabilisco la regex per controllare che l'autenticazione basica sia valida

            var authoHeaderRegEx = new Regex("Basic (.*)");

            if (!authoHeaderRegEx.IsMatch(authorizationHeader))
            {
                return Task.FromResult(AuthenticateResult.Fail("Authorization Code, not properly formatted"));
            }

            //Decodifico username e password e li metto in un array

            var authBase64 = Encoding.UTF8.GetString(Convert.FromBase64String(authoHeaderRegEx.Replace(authorizationHeader, "$1")));
            var authSplit = authBase64.Split(Convert.ToChar(":"), 2);

            var authUser = authSplit[0];
            var authPassword = authSplit.Length > 1 ? authSplit[1] : throw new Exception("Unable to get Password");

            //Apro la connessione al DB per confrontare Username e Password arrivati dal Front-end con quelli che si trovano nel DB

            bool userOk = false;
            var cnnLogin = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build().GetSection("ConnectionStrings")["LoginDb"];
            DBConnector db = new(cnnLogin.ToString());

            db.ConnectToDB();

            SqlCommand cmd = db.SqlCnn.CreateCommand();
            cmd.CommandText = "SELECT * From dbo.NewCustomer";
            cmd.CommandType = System.Data.CommandType.Text;

            //Qui faccio il confronto tra Email e password con il DB
            
            using(SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {

                    bool check = encryption.checkPassword(authPassword, reader["PasswordHash"].ToString(), reader["PasswordSalt"].ToString());
                    if (reader["EmailAddress"].ToString() == authUser && check == true) 
                    {
                        userOk = true;
                    }
                }
            }

            db.SqlCnn.Close();

            //Se non esistono EMAIL o Password nel DB allora interrompo se invece esistono confermo l'autenticazione

            if (!userOk)
            {
                return Task.FromResult(AuthenticateResult.Fail("User e/o password errati !!!"));
            }

            var authenticatedUser = new AuthenticatedUser("BasicAuthentication", true, "claudio");

            var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(authenticatedUser));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name)));

        }
    }
}
