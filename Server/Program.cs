﻿using MQTTnet;
using MQTTnet.Server;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Server
{
    internal class Program
    {
        public static async Task onNewClientConnection(ClientConnectedEventArgs e)
        {
            Console.WriteLine("New Client, calling OnNewClient ");  
                
        }
        public static X509Certificate2 CreateSelfSignedCertificate(string oid)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
            sanBuilder.AddDnsName("localhost");

            using (var rsa = RSA.Create())
            {
                var certRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);

                certRequest.CertificateExtensions.Add(
                    new X509KeyUsageExtension(X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature, false));

                certRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(oid) }, false));

                certRequest.CertificateExtensions.Add(sanBuilder.Build());

                using (var certificate = certRequest.CreateSelfSigned(DateTimeOffset.Now.AddMinutes(-10), DateTimeOffset.Now.AddMinutes(10)))
                {
                    var pfxCertificate = new X509Certificate2(
                        certificate.Export(X509ContentType.Pfx),
                        (string)null!,
                        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);

                    return pfxCertificate;
                }
            }
        }
        public static X509Certificate2 ReadCertificateFromFile(string filePath, string password = null)
        {
            try
            {
                return new X509Certificate2(filePath, password);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading certificate: {ex.Message} {ex.StackTrace}");
                return null;
            }
        }
        static async Task Main(string[] args)
        {
            var mqttFactory = new MqttFactory();


            string serverCertPath = "../../../PKI/Server/server.pfx";
            // WITH THIS DOES NOT WORK
            var certificate = ReadCertificateFromFile(serverCertPath,"password");
            
            // WITH THIS DOES WORK
            
            var certificate2 = CreateSelfSignedCertificate("1.3.6.1.5.5.7.3.1");

            var mqttServerOptions = new MqttServerOptionsBuilder().WithEncryptionCertificate(certificate.Export(X509ContentType.Pfx)).WithEncryptedEndpoint().Build();

            using (var mqttServer = mqttFactory.CreateMqttServer(mqttServerOptions))
            {
                mqttServer.ClientConnectedAsync += onNewClientConnection;
                try {
                    
                    await mqttServer.StartAsync();
                
                
                } catch (Exception ex) {
                    Console.WriteLine($"Error when client connecting: {ex.Message} {ex.StackTrace}");
                }

                Console.WriteLine("Waitng for connections \n Press Enter to exit.");
                Console.ReadLine();

                await mqttServer.StopAsync();
            }
        }
    }
}