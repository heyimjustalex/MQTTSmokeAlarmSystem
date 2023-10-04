﻿using Broker.Configuration;
using MQTTnet;
using MQTTnet.Server;
using Broker.Repository;
using Broker.Service;
using System.Security.Cryptography.X509Certificates;
using Broker.PKI;
using System.Threading.Tasks;
using System;
using MQTTnet.Client;
using System.Security.Authentication;
using System.Text;
using System.Net;
using System.Collections.Generic;
using Broker.Entity;
using System.Threading;
using Broker.Database;
using Server.Sensor;
using Newtonsoft.Json;
using MQTTnet.Protocol;
using UI;
using BrokerGUI.Message;
using BrokerGUI.Service;
using System.Windows.Documents;

namespace Broker.MqttManager
{
    class MqttManager
    {
        MqttFactory _mqttFactory;
        MqttServer _mqttServer; 
        MqttBrokerConfiguration _mqttBrokerConfiguration;
        MqttServerOptions _serverOptionsConfiguration;
        List<Client> _currentlyConnectedValidatedClients;
        IMessagePublisherService _messagePublisherService;
        ClientAccountService _clientAccountService;
        ActiveClientsService _activeClientsService;

        

        public MqttManager(MqttBrokerConfiguration configuration, ClientAccountService clientAccountService)
        {
            _currentlyConnectedValidatedClients = new List<Client>();    
            _mqttFactory = new MqttFactory();
            _messagePublisherService = new MessagePublisherService();
            _activeClientsService = new ActiveClientsService();
            _mqttBrokerConfiguration = configuration;
            _serverOptionsConfiguration = generateBrokerConfigurationOptions();
            _clientAccountService = clientAccountService;
            _mqttServer = _mqttFactory.CreateMqttServer(_serverOptionsConfiguration);
            initBrokerFunctionHandlers();       
        }     

        private MqttServerOptions generateBrokerConfigurationOptions()
        {
            return new MqttServerOptionsBuilder()
                .WithEncryptedEndpointPort(_mqttBrokerConfiguration.Port)
          
                .WithEncryptedEndpointBoundIPAddress(IPAddress.Parse(_mqttBrokerConfiguration.IpAddress))
                .WithEncryptionCertificate(_mqttBrokerConfiguration.Certificate.Export(X509ContentType.Pfx))
                .WithEncryptionSslProtocol(System.Security.Authentication.SslProtocols.Tls12)
                .WithEncryptedEndpoint()
                .Build();        
         }

        private void initBrokerFunctionHandlers()
        {
            _mqttServer.ClientConnectedAsync += onNewClientConnection;
            _mqttServer.InterceptingPublishAsync += onInterceptingPublishAsync;
            _mqttServer.ValidatingConnectionAsync += onClientConnectionValidation;
            _mqttServer.ClientDisconnectedAsync += onClientDisconnect;
        }
        private async Task onClientDisconnect(ClientDisconnectedEventArgs e)
        {
            Console.WriteLine($"Client {e.ClientId} disconnected"); 
            UI.GUIClientManager.RemoveClientByID(e.ClientId);
            _activeClientsService.removeClientById(e.ClientId);          
         
        }

        private void initalizeNewClient(string clientId, string username)
        {
            SensorData initialSmokeState = new SensorData("SMOKE", "FALSE");
            List<SensorData> initalList = new List<SensorData> { initialSmokeState };
            Client currentClient = new Client(clientId, username);
            currentClient.currentSensorDatas = initalList;
            _activeClientsService.add(currentClient);    
            UI.GUIClientManager.AddClient(new UI.ClientGUI(clientId, username, "FALSE", "FALSE"));
        }
        private async Task onClientConnectionValidation(ValidatingConnectionEventArgs e)
        {
            string clientId = e.ClientId;            
            string username = e.UserName;      
            string password = e.Password?.ToString() ?? string.Empty;       

            if (clientId == null || username == null || password == null)
            {
                return;
            }

            if (username != null)
            {
                if (!_clientAccountService.authenticate(username, password))
                {
                    Console.WriteLine($"Authenticating with username: {username} FAILED");
                    e.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;
                    e.ReasonString = "Invalid client identifier.";
                    
                    return ;
                }
            }
            
            Console.WriteLine($"onClientConnectionValidation: ClientId: {clientId}, Username: {username} authenticated");
            // initialize that no smoke is detected
            // after milisecond message will come and update the state of this collection initial list that is inside client
            // no worries


            initalizeNewClient(clientId, username);



        }
        private async Task onNewClientConnection(ClientConnectedEventArgs e)
        {
            Console.WriteLine("onNewClientConnection: New Client has been detected ");   
            
        }

        public void kill()
        {
            _mqttServer.Dispose();
        }
        private async Task enqueueToAllSpecifiedTopics(List<SensorData> sensorData)
        {
            var mqttMessage = new MessageMQTT(DateTime.Now, "broker", sensorData);
            string json = System.Text.Json.JsonSerializer.Serialize(mqttMessage);
            Console.WriteLine($"BROKER: enqueue to alarm/fromBroker: {mqttMessage.ToString()}");
                     
            foreach (var topic in _mqttBrokerConfiguration.TopicsBrokerEnqueuesTo)
            {
                var message = new MqttApplicationMessageBuilder()
                  .WithTopic(topic)
                  .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                  .WithMessageExpiryInterval(1000)
                  .WithRetainFlag(true)
                  .WithPayload(json)
                  .Build();                
                   await _mqttServer.InjectApplicationMessage(new InjectedMqttApplicationMessage(message));

            }
        }

        private Client? GetClientByClientId(string clientId)
        {
            foreach (Client client in _currentlyConnectedValidatedClients)
            {
                if(client.clientId == clientId)
                {
                    return client;
                }
            }
            return null;
        }
        private SensorData getSmokeDetectorData(List<SensorData> sensorDatas) {

            SensorData ret = sensorDatas[0];
            foreach (SensorData sensorData in sensorDatas)
            {
                if(sensorData.ParameterName=="SMOKE")
                {
                    ret = sensorData;
                }
            }
            return ret;
        }

        private List<SensorData> updateBuzzerSensor(List<SensorData> sensorDatas, string buzzerParameterValue)
        {
     
            foreach (SensorData sensorData in sensorDatas)
            {
                if(sensorData.ParameterName=="BUZZER")
                {             
                    sensorData.ParameterValue = buzzerParameterValue;
                    return sensorDatas;
                }
            }

            //if there was no buzzer found in the collention of elements
            sensorDatas.Add(new SensorData("BUZZER", buzzerParameterValue));

            return sensorDatas;
        }

        private List<SensorData> updateSmokerSensor(List<SensorData> sensorDatas, string smokeParameterValue)
        {
            foreach (SensorData sensorData in sensorDatas)
            {
                if (sensorData.ParameterName == "SMOKE")
                {
                    sensorData.ParameterValue = smokeParameterValue;
                    return sensorDatas;
                }
            }

            //if there was no smoke found in the collention of elements
            sensorDatas.Add(new SensorData("SMOKE", smokeParameterValue));

            return sensorDatas;
        }

        private List<SensorData> rewriteListModifyingParameter(List<SensorData> sensorDatas, string ParamName, string ParamValue) {
            
            foreach (SensorData sensorData in sensorDatas)
            {
                if(sensorData.ParameterName==ParamName)
                {
                    sensorData.ParameterValue = ParamValue; 
                }
            }           
            return sensorDatas;        
        
        }

        private async Task turnOnBuzzerOfAllClients()
        {
            List<SensorData> buzzerTrueInformMessage = new List<SensorData>
                            {
                                new SensorData("BUZZER", "TRUE")
                            };

            await enqueueToAllSpecifiedTopics(buzzerTrueInformMessage);
        }
     
        private void updateAllClientsBuzzerTrueAndSmokeDetectorOfReceiverTrue(string receiverClientId)
        {
            _activeClientsService.updateClients((client) =>
            {
                foreach (SensorData sensorData in client.currentSensorDatas)
                {
                    if (client.clientId == receiverClientId)
                    {
                        if (sensorData.ParameterValue == "BUZZER")
                        {
                            sensorData.ParameterValue = "TRUE";
                        }
                    }

                    if (sensorData.ParameterValue == "SMOKE")
                    {
                        sensorData.ParameterValue = "TRUE";
                    }
                }
            });
        }

        private void updateGUIAllClientsBuzzerTrueAndSmokeDetectorOfReceiverTrue(string receiverClientId)
        {
            UI.GUIClientManager.updateClients((client) => {

                if (client.clientId == receiverClientId)
                {
                    client.smokeDetectorState = "TRUE";
                }
                client.buzzerState = "TRUE";

            });
        }

        private void updateClientReportingNosmokeSmokeDetectorToFalse(string receiverClientId) {
            _activeClientsService.updateClients((client) =>
            {
                if (client.clientId == receiverClientId)
                {
                    foreach (SensorData sensorData in client.currentSensorDatas)
                    {
                        if (sensorData.ParameterValue == "SMOKE")
                        {
                            sensorData.ParameterValue = "FALSE";
                        }
                    }

                }
            });

        }
        private void updateGUIClientReportingNosmokeSmokeDetectorToFalse(string receiverClientId) {


            UI.GUIClientManager.updateClients((client) => {

                if (client.clientId == receiverClientId)
                {
                    client.smokeDetectorState = "FALSE";
                }
            });

        }

        private void updateGUIClientsBuzzerAndSmokeDetectorToFalse()
        {
            UI.GUIClientManager.updateClients(
                              (client) => {
                                  client.smokeDetectorState = "FALSE"; client.buzzerState = "FALSE";


                              });
        }
        private async Task disableBuzzersOfAllClients()
        {
            List<SensorData> buzzerFalseInformMessage = new List<SensorData>
                            {
                                new SensorData("BUZZER", "FALSE")
                            };
            await enqueueToAllSpecifiedTopics(buzzerFalseInformMessage);
        }

        private async Task handleMessageFromClient(string clientId, List<SensorData> sensorDatas)
        {
            SensorData smokeSensorData = getSmokeDetectorData(sensorDatas);

            // LOGIC OF HANDLING CLIENT MESSAGE
            // IF THERE IS SMOKE: 1. Take all authenticated clients and send them {BUZZER:TRUE} | 2. Update local authenticated clients list and GUI
            // IF THERE IS NO SMOKE: 1. Set client's smoke sensor to false {SMOKE:FALSE}, update GUI (just one smoke sensor of client)
            //                       2. If it's the last smoke sensor that was on disable alarm (Send {BUZZER:FALSE}), update GUI

            if (smokeSensorData.ParameterValue == "TRUE")
            {

                await turnOnBuzzerOfAllClients();
                updateAllClientsBuzzerTrueAndSmokeDetectorOfReceiverTrue(clientId);
                updateGUIAllClientsBuzzerTrueAndSmokeDetectorOfReceiverTrue(clientId);

            }
            else
            {
                updateClientReportingNosmokeSmokeDetectorToFalse(clientId);
                
                // if smoke detector is down everywhere (if fire didnt spread)
                if (!_activeClientsService.doAnyClientsHaveSmoke())
                {
                    await disableBuzzersOfAllClients();
                    updateGUIClientsBuzzerAndSmokeDetectorToFalse();

                }
                else
                {
                    updateGUIClientReportingNosmokeSmokeDetectorToFalse(clientId);

                }
            }
        }
        private async Task onInterceptingPublishAsync(InterceptingPublishEventArgs e)
        {
            var applicationMessage = e.ApplicationMessage;

            if (applicationMessage == null) 
            { 
                return; 
            }

            var topic = applicationMessage.Topic;       

            var payloadText = string.Empty;
            if (e.ApplicationMessage.PayloadSegment.Count <= 0)
            {
                return;
            }

            payloadText = Encoding.UTF8.GetString(
                   e.ApplicationMessage.PayloadSegment.Array,
                   e.ApplicationMessage.PayloadSegment.Offset,
                   e.ApplicationMessage.PayloadSegment.Count);

            MessageMQTT message = JsonConvert.DeserializeObject<MessageMQTT>(payloadText);

           if(message == null) {
                return;
            }

            if (message.From != "broker")
            {
                Console.WriteLine($"Broker: Received publish request from client on topic '{topic}': {message.ToString()}");

                string clientId = message.From;

                if(message.SensorDatas.Count>0)
                {
                  await handleMessageFromClient(clientId,message.SensorDatas);
                    
                }

                
            }

            await Task.CompletedTask;
        }

       
        public async Task start(CancellationToken cancellationToken)
        {
            try
            {
               await _mqttServer.StartAsync();
                        

                int i = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (i % 60 == 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] I'm broker and I'm working properly");
                        i = 0;
                    }
                    i++;    
                    Thread.Sleep(5000);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error when client connecting: {ex.Message} {ex.StackTrace}");
            }
  
        }

        public async Task publishMessage(string topic, string payload, string brokerId)
        {
            await _messagePublisherService.publishMessageAsync(_mqttServer, topic, payload, brokerId);
        }    
        


        public async Task DisconnectClientAsync(string clientId)
        {
            if (_mqttServer.IsStarted)
            {
               
                await _mqttServer.DisconnectClientAsync(clientId,MQTTnet.Protocol.MqttDisconnectReasonCode.AdministrativeAction);
            }
            else
            {
             
                Console.WriteLine("MQTT server is not started.");
            }
        }
    }
}
