﻿using System;
using System.Net;
using System.Threading.Tasks;
using Broker.Configuration;
using Broker.Database;
using Broker.PKI;
using Broker.Repository;
using Broker.Service;
using Broker.MqttManager;
using System.Reflection.PortableExecutable;
using System.Threading;

namespace BrokerGUI
{
    class Program
    {
        // static MqttServer mqttServer;
       public static async Task<Task> Broker(CancellationToken cancellationToken)
        {
            Console.WriteLine("Starting new broker");
            IClientDB clientDB = new ClientDB();
            IClientRepository clientRepository = new ClientRepository(clientDB);
            IClientService clientService = new ClientService(clientRepository);

            string serverCertPath = "../../../PKI/Broker/broker1.pfx";
            string keyCertPath = "../../../PKI/Broker/key1.pem";

            var certificate = PKIUtilityStatic.ReadCertificateWithPrivateKey(serverCertPath, keyCertPath, "password");

            MqttBrokerConfiguration configuration = new MqttBrokerConfigurationBuilder()
            .WithPort(8883)
            .WithIpAddress("127.0.0.1")
            .WithId("broker1")
            .WithTopicsBrokerEnqueuesTo(new string[] { "alarm/fromBroker" })
            .WithTopicsBrokerSubscribesTo(new string[] { "alarm/fromClient" })
            .WithCertificate(certificate)
            .Build();

            MqttManager mqttManager = new MqttManager(configuration, clientService);
      
           
            await  mqttManager.start(cancellationToken);

            mqttManager.kill();
            Console.WriteLine("Broker has been killed");
            Thread.Sleep(1000);
            Console.Clear();
            return Task.CompletedTask;
            

            //await Task.Run(mqttBroker.startAsync);
            //await Task.Run(mqttBroker.init);
            //Console.WriteLine("MQTT Server has started. Enter to send mess!");
            //Console.ReadLine();
            //await mqttBroker.publishMessage("Test", "testMessage", mqttBrokerConfig.Id);

            //Console.WriteLine("Enter to stop!");
            //Console.ReadLine();

            //await mqttBroker.stopAsync();

        }
    }
}