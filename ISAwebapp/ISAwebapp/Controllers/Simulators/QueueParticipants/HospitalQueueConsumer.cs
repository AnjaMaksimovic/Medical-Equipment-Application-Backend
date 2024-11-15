﻿using API.Controllers.Simulators.Models;
using ISAProject.Modules.Company.API.Dtos;
using ISAProject.Modules.Company.API.Public;
using ISAProject.Modules.Company.Core.Domain;
using ISAProject.Modules.Company.Core.Domain.RepositoryInterfaces;
using ISAProject.Modules.Company.Infrastructure.Database.Repositories;
using ISAProject.Modules.Stakeholders.Core.Domain.RepositoryInterfaces;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Net.Http;
using System.Text;
using IModel = RabbitMQ.Client.IModel;

namespace API.Controllers.Simulators.QueueParticipants
{
    public class HospitalQueueConsumer
    {
        private readonly HttpClient _httpClient;

        public HospitalQueueConsumer(IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClientFactory.CreateClient();
        }

        public  void ReceiveContract(IModel channel)
        {

            channel.QueueDeclare("receiving-delivery-queue",
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += async (sender, e) =>
            {
                var body = e.Body.ToArray();
                var messageJson = Encoding.UTF8.GetString(body);
                var contract = Newtonsoft.Json.JsonConvert.DeserializeObject<HospitalContract>(messageJson);
                var contractJson = JsonConvert.SerializeObject(contract);

                await SendContractDataToEndpoint(contract);

            };

            channel.BasicConsume("receiving-delivery-queue", true, consumer);
        }

        private async Task SendContractDataToEndpoint(HospitalContract contract)
        {
                var createRequest = new HttpRequestMessage(HttpMethod.Post, "https://localhost:44310/api/contract");
                var createContent = new StringContent(JsonConvert.SerializeObject(contract), Encoding.UTF8, "application/json");
                createRequest.Content = createContent;

                var createResponse = await _httpClient.SendAsync(createRequest);
                createResponse.EnsureSuccessStatusCode();
        }

    }
}
