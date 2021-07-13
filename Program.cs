using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using RestSharp;
using Newtonsoft.Json;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Linq;

namespace ConsoleApp2
{
    class Program
    {
        public static string _coinBaseApiUrl = string.Empty;
        public static string _coinBaseApiKey = string.Empty;
        public static string _coinBaseApiSecret = string.Empty;
        public static string _coinBaseBuyPrices = string.Empty;
        public static string _coinBaseSellPrices = string.Empty;
        public static string _buyAmountUSD = string.Empty;
        public static string _buyAmount = string.Empty;
        public static decimal _floorPrice;
        public static decimal _ceilingPrice;
        public static decimal _sellPercentage;

        static void Main(string[] args)
        {
            var appSettings = ConfigurationManager.AppSettings;
            _coinBaseApiUrl = appSettings.Get("CoinBaseAPI");
            _coinBaseBuyPrices = appSettings.Get("CoinBaseBuyPrice");
            _coinBaseSellPrices = appSettings.Get("CoinBaseSellPrice");
            _coinBaseApiKey = appSettings.Get("ApiKey");
            _coinBaseApiSecret = appSettings.Get("ApiSecret");
            _floorPrice = Convert.ToDecimal(appSettings.Get("FloorPrice"));
            _ceilingPrice = Convert.ToDecimal(appSettings.Get("CeilingPrice"));
            _buyAmountUSD = appSettings.Get("BuyAmountUSD");
            _buyAmount = appSettings.Get("BuyAmount");
            _sellPercentage = Convert.ToDecimal(appSettings.Get("SellPercentage"));

            string paymentMethods = appSettings.Get("PaymentMethods");

            decimal floorPrice = _floorPrice;
            decimal priceBought = decimal.MinValue;

            string accountId = GetAccountId();
            string paymentMethodId = GetPaymentMethod(paymentMethods);

            while (floorPrice >= _floorPrice)
            {
                var client = new RestClient(_coinBaseApiUrl);
                var request = new RestRequest(_coinBaseBuyPrices, Method.GET);
                var queryResult = client.Execute<string>(request).Data;

                dynamic buyResults = JsonConvert.DeserializeObject<dynamic>(queryResult);

                request = new RestRequest(_coinBaseSellPrices, Method.GET);
                queryResult = client.Execute<string>(request).Data;
                dynamic sellResults = JsonConvert.DeserializeObject<dynamic>(queryResult);

                Console.Write("\r{0}", "One BTC Buy Price: $" + String.Format("{0:n}", Convert.ToDecimal(buyResults.data.amount)) + @" ----- One BTC Sell Price: $"
                + String.Format("{0:n}", Convert.ToDecimal(sellResults.data.amount)) + @" ----- Buy/Sell Difference: $" + (Convert.ToDecimal(buyResults.data.amount)
                - Convert.ToDecimal(sellResults.data.amount)).ToString());

                floorPrice = buyResults.data.amount;

                System.Threading.Thread.Sleep(7000);
            }

            if (floorPrice < _floorPrice)
            {
                var buyBitcoin = MakeBuy(paymentMethodId, accountId);
                priceBought = floorPrice;
            }

            while (floorPrice < (priceBought * _sellPercentage))
            {
                var client = new RestClient(_coinBaseApiUrl);
                var request = new RestRequest(_coinBaseBuyPrices, Method.GET);
                var queryResult = client.Execute<string>(request).Data;

                dynamic buyResults = JsonConvert.DeserializeObject<dynamic>(queryResult);

                request = new RestRequest(_coinBaseSellPrices, Method.GET);
                queryResult = client.Execute<string>(request).Data;
                dynamic sellResults = JsonConvert.DeserializeObject<dynamic>(queryResult);

                Console.Write("\r{0}", "One BTC Buy Price: $" + String.Format("{0:n}", Convert.ToDecimal(buyResults.data.amount)) + @" ----- One BTC Sell Price: $"
                + String.Format("{0:n}", Convert.ToDecimal(sellResults.data.amount)) + @" ----- Buy/Sell Difference: $" + (Convert.ToDecimal(buyResults.data.amount)
                - Convert.ToDecimal(sellResults.data.amount)).ToString());

                floorPrice = buyResults.data.amount;

                System.Threading.Thread.Sleep(7000);
            }

            if (floorPrice >= (priceBought * _sellPercentage))
            {
                var sellBitcoin = MakeSale(paymentMethodId, accountId);
            }

            Console.Clear();
            Console.WriteLine("Price Floor Reached...");
            Console.ReadLine();
        }

        public static string GetPaymentMethod(string paymentMethods)
        {
            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            var paymentMethod = new RestClient(_coinBaseApiUrl);
            var paymentMethodRequest = new RestRequest(paymentMethods, Method.GET);
            paymentMethod.AddDefaultHeader("CB-ACCESS-KEY", _coinBaseApiKey);
            paymentMethod.AddDefaultHeader("CB-ACCESS-SIGN", GetAccessSign(timestamp, "GET", paymentMethods, string.Empty, _coinBaseApiSecret));
            paymentMethod.AddDefaultHeader("CB-ACCESS-TIMESTAMP", timestamp);
            var paymentMethodResult = paymentMethod.Execute<string>(paymentMethodRequest).Data;

            dynamic paymentMethodResults = JsonConvert.DeserializeObject<dynamic>(paymentMethodResult);
            var paymentId = string.Empty;

            foreach (dynamic paymentMeth in paymentMethodResults.data)
            {
                paymentId = paymentMeth.id;

                if (paymentMeth.primary_buy == true)
                {
                    break;
                }
            }

            return paymentId;
        }

        public static string GetAccountId()
        {
            string accountsEndpoint = "/v2/accounts";

            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            var account = new RestClient(_coinBaseApiUrl);
            var accountRequest = new RestRequest(accountsEndpoint, Method.GET);
            account.AddDefaultHeader("CB-ACCESS-KEY", _coinBaseApiKey);
            account.AddDefaultHeader("CB-ACCESS-SIGN", GetAccessSign(timestamp, "GET", accountsEndpoint, string.Empty, _coinBaseApiSecret));
            account.AddDefaultHeader("CB-ACCESS-TIMESTAMP", timestamp);
            var accountResult = account.Execute<string>(accountRequest).Data;

            dynamic accountResults = JsonConvert.DeserializeObject<dynamic>(accountResult);
            var accountId = string.Empty;

            foreach (dynamic accountSingle in accountResults.data)
            {
                accountId = accountSingle.id;

                break;
            }

            return accountId;
        }

        public static string GetAccessSign(string timestamp, string command, string path, string body, string apiSecret)
        {
            var hmacKey = Encoding.UTF8.GetBytes(apiSecret);

            string data = timestamp + command + path + body;
            using (var signatureStream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                return new HMACSHA256(hmacKey).ComputeHash(signatureStream).Aggregate(new StringBuilder(), (sb, b) => sb.AppendFormat("{0:x2}", b), sb => sb.ToString());
            }
        }

        public static string MakeBuy(string paymentMethod, string accountId)
        {
            string buyEndpoint = $"/v2/accounts/{accountId}/buys";
            var body = new { currency = "BTC", total = _buyAmountUSD, payment_method = paymentMethod };
            var bodySerialized = JsonConvert.SerializeObject(body);
            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            var buy = new RestClient(_coinBaseApiUrl);
            var buyRequest = new RestRequest(buyEndpoint, Method.POST);
            buyRequest.AddJsonBody(body);

            buy.AddDefaultHeader("CB-ACCESS-KEY", _coinBaseApiKey);
            buy.AddDefaultHeader("CB-ACCESS-SIGN", GetAccessSign(timestamp, "POST", buyEndpoint, bodySerialized, _coinBaseApiSecret));
            buy.AddDefaultHeader("CB-ACCESS-TIMESTAMP", timestamp);

            var buyResult = buy.Execute<string>(buyRequest).Data;

            dynamic buyResults = JsonConvert.DeserializeObject<dynamic>(buyResult);

            return buyResult;
        }

        public static string MakeSale(string paymentMethod, string accountId)
        {
            string buyEndpoint = $"/v2/accounts/{accountId}/sells";
            var body = new { currency = "USD", amount = _buyAmountUSD, payment_method = paymentMethod };
            var bodySerialized = JsonConvert.SerializeObject(body);
            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
            var buy = new RestClient(_coinBaseApiUrl);
            var buyRequest = new RestRequest(buyEndpoint, Method.POST);
            buyRequest.AddJsonBody(body);

            buy.AddDefaultHeader("CB-ACCESS-KEY", _coinBaseApiKey);
            buy.AddDefaultHeader("CB-ACCESS-SIGN", GetAccessSign(timestamp, "POST", buyEndpoint, bodySerialized, _coinBaseApiSecret));
            buy.AddDefaultHeader("CB-ACCESS-TIMESTAMP", timestamp);

            var buyResult = buy.Execute<string>(buyRequest).Data;

            dynamic buyResults = JsonConvert.DeserializeObject<dynamic>(buyResult);

            return buyResult;

        }
    }
}

