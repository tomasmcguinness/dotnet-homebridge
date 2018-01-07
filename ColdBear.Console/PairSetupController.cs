﻿using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using SRP;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace ColdBear.ConsoleApp
{
    public class PairSetupController : ApiController
    {
        private static SRPServer sessionServer;
        private static string CODE;
        private static byte[] salt;

        private static byte[] server_k;
        private static byte[] server_x;
        private static System.Numerics.BigInteger server_v;
        private static byte[] server_b;
        private static System.Numerics.BigInteger server_B;

        public async Task<HttpResponseMessage> Post()
        {
            var body = await Request.Content.ReadAsByteArrayAsync();

            Debug.WriteLine($"Length of input is {body.Length} bytes");

            var parts = TLVParser.Parse(body);

            var state = parts.GetTypeAsInt(Constants.State);

            Debug.WriteLine($"Pair Setup: Status [{state}]");

            if (state == 1)
            {
                Debug.WriteLine("Pair Setup starting");
                Debug.WriteLine("SRP Start Response");

                Random randomNumber = new Random();
                int code = randomNumber.Next(100, 999);

                CODE = $"123-45-{code}";

                Console.WriteLine($"PING CODE: {CODE}");

                Random rnd = new Random();
                salt = new Byte[16];
                rnd.NextBytes(salt);

                // **** BOUNCY CASTLE CODE - NOT USED ****
                //https://www.programcreek.com/java-api-examples/index.php?api=org.bouncycastle.crypto.agreement.srp.SRP6Server

                var I = "Pair-Setup";// Program.ID;
                var P = CODE;

                var hashAlgorithm = SHA512.Create();
                var groupParameter = SRP.SRP.Group_3072;

                sessionServer = new SRPServer(groupParameter, hashAlgorithm);

                server_k = sessionServer.Compute_k();

                server_x = sessionServer.Compute_x(salt, I, P);

                server_v = sessionServer.Compute_v(server_x.ToBigInteger());

                Console.WriteLine($"Verifier [Length={server_v.ToBytes().Length}]");
                Console.WriteLine(server_v.ToString("X"));

                server_b = new Byte[32];
                rnd.NextBytes(server_b);

                server_B = sessionServer.Compute_B(server_v, server_k.ToBigInteger(), server_b.ToBigInteger());

                Console.WriteLine($"B [Length={server_B.ToBytes().Length}]");
                Console.WriteLine(server_B.ToString("X"));

                var publicKey = server_B.ToBytes();

                TLV responseTLV = new TLV();

                responseTLV.AddType(Constants.State, 2);
                responseTLV.AddType(Constants.PublicKey, publicKey);
                responseTLV.AddType(Constants.Salt, salt);

                byte[] output = TLVParser.Serialise(responseTLV);

                ByteArrayContent content = new ByteArrayContent(output);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pairing+tlv8");

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = content
                };
            }
            else if (state == 3)
            {
                Debug.WriteLine("SRP Verify Request");

                var iOSPublicKey = parts.GetType(Constants.PublicKey); // A 
                var iOSProof = parts.GetType(Constants.Proof); // M1

                Console.WriteLine("A");
                Console.WriteLine(ByteArrayToString(iOSPublicKey));

                Console.WriteLine("M1 (Client)");
                Console.WriteLine(ByteArrayToString(iOSProof));

                // Compute the scrambler.
                //
                var u = sessionServer.Compute_u(iOSPublicKey, server_B.ToBytes());

                Console.WriteLine("U (Scramber)");
                Console.WriteLine(ByteArrayToString(u));

                // Compute the premaster secret
                //
                var server_S = sessionServer.Compute_S(iOSPublicKey.ToBigInteger(), server_v, u.ToBigInteger(), server_b.ToBigInteger());
                Console.WriteLine("S");
                Console.WriteLine(server_S.ToString("X"));

                // Compute the session key
                //
                var server_K = sessionServer.Compute_K(server_S.ToBytes());

                Console.WriteLine("K (Session Key)");
                Console.WriteLine(ByteArrayToString(server_K));

                // Compute the client's proof
                //
                var client_M1 = sessionServer.Compute_M1("Pair-Setup", salt, iOSPublicKey, server_B.ToBytes(), server_K);

                Console.WriteLine("M1 (Server)");
                Console.WriteLine(ByteArrayToString(client_M1));

                // Check the proof matches what was sent to us
                //
                bool isValid = iOSProof.CheckEquals(client_M1);

                TLV responseTLV = new TLV();
                responseTLV.AddType(Constants.State, 4);

                if (isValid)
                {
                    Console.WriteLine("Verification was successful. Generating Server Proof (M2)");

                    var server_M2 = sessionServer.Compute_M2(iOSPublicKey, client_M1, server_K);

                    responseTLV.AddType(Constants.Proof, server_M2);
                }
                else
                {
                    Console.WriteLine("Verification failed as iOS provided code was incorrect");

                    responseTLV.AddType(Constants.Error, ErrorCodes.Authentication);
                }

                byte[] output = TLVParser.Serialise(responseTLV);

                ByteArrayContent content = new ByteArrayContent(output);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pairing+tlv8");

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = content
                };
            }
            else if (state == 5)
            {
                Debug.WriteLine("Exchange Response");



            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
        }

        private BigInteger FromHex(string hex)
        {
            return new BigInteger(1, StringToByteArray(hex));
        }

        public static byte[] StringToByteArray(String hex)
        {
            hex = hex.Replace(" ", "");
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            {
                hex.AppendFormat("{0:x2}", b);
            }
            return hex.ToString().ToUpper();
        }
    }
}
