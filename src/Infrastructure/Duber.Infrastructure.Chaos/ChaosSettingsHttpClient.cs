﻿using Duber.Infrastructure.Http;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Duber.Infrastructure.Chaos
{
    public class ChaosApiHttpClient
    {
        private readonly HttpClient _client;

        public ChaosApiHttpClient(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<GeneralChaosSetting> GetGeneralChaosSettings()
        {
            var response = await _client.GetAsync("/");
            response.EnsureSuccessStatusCode();
            return JsonConvert.DeserializeObject<GeneralChaosSetting>(await response.Content.ReadAsStringAsync());
        }

        public async Task UpdateGeneralChaosSettings(GeneralChaosSetting settings)
        {
            var response = await _client.PostAsync("/", new JsonContent(settings));
            response.EnsureSuccessStatusCode();            
        }
    }
}