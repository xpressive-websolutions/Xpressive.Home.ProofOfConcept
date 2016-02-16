using System.Collections.Generic;

namespace Xpressive.Home.ProofOfConcept.Gateways.TextToSpeech
{
    internal class TextToSpeechGatewayFactory : IGatewayDeviceFactory<TextToSpeechGateway>
    {
        public IEnumerable<string> GetPropertiesForCreation()
        {
            yield return "Api Key";
        }

        public bool TryCreate(IGateway gateway, IDictionary<string, string> properties, out IDevice device)
        {
            device = null;
            string apiKey;

            if (!properties.TryGetValue("Api Key", out apiKey))
            {
                return false;
            }

            device = ((TextToSpeechGateway)gateway).AddDevice(new TextToSpeechDevice(apiKey));
            return true;
        }
    }
}