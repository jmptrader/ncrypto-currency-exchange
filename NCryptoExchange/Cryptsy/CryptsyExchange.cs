﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using Lostics.NCryptoExchange.Model;

namespace Lostics.NCryptoExchange.Cryptsy
{
    public class CryptsyExchange : IExchange<CryptsyMarketId, CryptsyOrderId, CryptsyTradeId, Wallet>
    {
        public const string HEADER_SIGN = "Sign";
        public const string HEADER_KEY = "Key";

        public const string PARAM_MARKET_ID = "marketid";
        public const string PARAM_METHOD = "method";
        public const string PARAM_LIMIT = "limit";
        public const string PARAM_NONCE = "nonce";
        public const string PARAM_ORDER_ID = "orderid";
        public const string PARAM_ORDER_TYPE = "ordertype";
        public const string PARAM_PRICE = "price";
        public const string PARAM_QUANTITY = "quantity";

        private HttpClient client = new HttpClient();
        private readonly string publicUrl = "http://pubapi.cryptsy.com/api.php";
        private readonly string privateUrl = "https://www.cryptsy.com/api";
        private readonly string publicKey;
        private readonly byte[] privateKey;

        public byte[] PrivateKey { get { return this.privateKey; } }

        public CryptsyExchange(string publicKey, string privateKey)
        {
            this.publicKey = publicKey;
            this.privateKey = System.Text.Encoding.ASCII.GetBytes(privateKey);
        }

        public async Task CancelOrder(CryptsyOrderId orderId)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.cancelorder,
                orderId, (CryptsyMarketId)null, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            await GetReturnAsJToken(response);
        }

        public async Task CancelAllOrders()
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.cancelallorders,
                (CryptsyOrderId)null, (CryptsyMarketId)null, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            await GetReturnAsJToken(response);
        }

        public async Task CancelMarketOrders(CryptsyMarketId marketId)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.cancelmarketorder,
                (CryptsyOrderId)null, marketId, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            await GetReturnAsJToken(response);
        }

        public async Task<Fees> CalculateFees(OrderType orderType, Quantity quantity,
                Quantity price)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateOrderRequest(CryptsyMethod.calculatefees,
                orderType, quantity, price));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JObject returnObj = (JObject)await GetReturnAsJToken(response);

            return new Fees(Quantity.Parse(returnObj["fee"]),
                Quantity.Parse(returnObj["net"]));
        }

        public async Task<CryptsyOrderId> CreateOrder(CryptsyMarketId marketId,
                OrderType orderType, Quantity quantity,
                Quantity price)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateOrderRequest(CryptsyMethod.createorder,
                orderType, quantity, price));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JObject returnObj = (JObject)await GetReturnAsJToken(response);

            return new CryptsyOrderId(returnObj["orderid"].ToString());
        }

        public void Dispose() {
            this.client.Dispose();
        }

        public async Task<Address> GenerateNewAddress(string currencyCode)
        {
            throw new NotImplementedException();
        }

        private KeyValuePair<string, string>[] GenerateRequest(CryptsyMethod method,
            CryptsyOrderId orderId, CryptsyMarketId marketId, int? limit)
        {
            List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>(PARAM_METHOD, System.Enum.GetName(typeof(CryptsyMethod), method)),
                new KeyValuePair<string, string>(PARAM_NONCE, GetNextNonce())
            };

            if (null != marketId)
            {
                parameters.Add(new KeyValuePair<string, string>(PARAM_MARKET_ID, marketId.ToString()));
            }

            if (null != orderId)
            {
                parameters.Add(new KeyValuePair<string, string>(PARAM_MARKET_ID, orderId.ToString()));
            }

            if (null != limit)
            {
                parameters.Add(new KeyValuePair<string, string>(PARAM_LIMIT, limit.ToString()));
            }

            return parameters.ToArray();
        }

        private KeyValuePair<string, string>[] GenerateOrderRequest(CryptsyMethod method,
            OrderType orderType, Quantity quantity, Quantity price)
        {
            return new[] {
                new KeyValuePair<string, string>(PARAM_METHOD, System.Enum.GetName(typeof(CryptsyMethod), method)),
                new KeyValuePair<string, string>(PARAM_NONCE, GetNextNonce()),
                new KeyValuePair<string, string>(PARAM_ORDER_TYPE, orderType.ToString()),
                new KeyValuePair<string, string>(PARAM_QUANTITY, quantity.ToString()),
                new KeyValuePair<string, string>(PARAM_PRICE, price.ToString())
            };
        }

        public async Task<AccountInfo<Wallet>> GetAccountInfo()
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.getinfo,
                (CryptsyOrderId)null, (CryptsyMarketId)null, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JObject returnObj = (JObject)await GetReturnAsJToken(response);

            return CryptsyAccountInfo.Parse(returnObj);
        }

        public string GetNextNonce()
        {
            return DateTime.Now.Ticks.ToString();
        }

        public async Task<List<MarketOrder>> GetMarketOrders(CryptsyMarketId marketId)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.marketorders,
                (CryptsyOrderId)null, marketId, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JObject returnObj = (JObject)await GetReturnAsJToken(response);

            List<MarketOrder> buyOrders = ParseMarketOrders(OrderType.Buy, (JArray)returnObj["buyorders"]);
            List<MarketOrder> sellOrders = ParseMarketOrders(OrderType.Sell, (JArray)returnObj["sellorders"]);

            buyOrders.AddRange(sellOrders);

            return buyOrders;
        }

        public async Task<List<Market<CryptsyMarketId>>> GetMarkets()
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.getmarkets,
                (CryptsyOrderId)null, (CryptsyMarketId)null, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JArray returnArray = (JArray)await GetReturnAsJToken(response);
            List<Market<CryptsyMarketId>> markets = new List<Market<CryptsyMarketId>>();

            foreach (JToken marketToken in returnArray)
            {
                markets.Add(CryptsyMarket.Parse(marketToken));
            }

            return markets;
        }

        public async Task<List<Transaction>> GetMyTransactions()
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.mytransactions,
                (CryptsyOrderId)null, (CryptsyMarketId)null, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JArray returnArray = (JArray)await GetReturnAsJToken(response);

            return ParseTransactions(returnArray);
        }

        public async Task<List<MarketTrade<CryptsyMarketId, CryptsyTradeId>>> GetMarketTrades(CryptsyMarketId marketId)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.markettrades,
                (CryptsyOrderId)null, marketId, null));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JArray returnArray = (JArray)await GetReturnAsJToken(response);

            return ParseMarketTrades(returnArray, marketId);
        }

        public async Task<List<MyTrade<CryptsyMarketId, CryptsyOrderId, CryptsyTradeId>>> GetMyTrades(CryptsyMarketId marketId, int? limit)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.mytrades,
                (CryptsyOrderId)null, marketId, limit));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JArray returnArray = (JArray)await GetReturnAsJToken(response);

            return ParseMyTrades(returnArray, marketId);
        }

        public async Task<List<MyTrade<CryptsyMarketId, CryptsyOrderId, CryptsyTradeId>>> GetAllMyTrades(int? limit)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.allmytrades,
                (CryptsyOrderId)null, (CryptsyMarketId)null, limit));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JArray returnArray = (JArray)await GetReturnAsJToken(response);

            return ParseMyTrades(returnArray, null);
        }

        public async Task<List<MyOrder<CryptsyMarketId, CryptsyOrderId>>> GetMyOrders(CryptsyMarketId marketId, int? limit)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.myorders,
                (CryptsyOrderId)null, marketId, limit));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JArray returnArray = (JArray)await GetReturnAsJToken(response);

            return ParseMyOrders(returnArray, marketId);
        }

        public async Task<List<MyOrder<CryptsyMarketId, CryptsyOrderId>>> GetAllMyOrders(int? limit)
        {
            FormUrlEncodedContent request = new FormUrlEncodedContent(GenerateRequest(CryptsyMethod.allmyorders,
                (CryptsyOrderId)null, (CryptsyMarketId)null, limit));

            await SignRequest(request);
            HttpResponseMessage response = await client.PostAsync(privateUrl, request);
            JArray returnArray = (JArray)await GetReturnAsJToken(response);

            return ParseMyOrders(returnArray, null);
        }

        public async Task<List<MarketDepth>> GetMarketDepth(CryptsyMarketId marketId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the "return" property from the response from Cryptsy, and returns
        /// it as a JToken. In case of an error response, throws a suitable Exception
        /// instead.
        /// </summary>
        /// <param name="response">The HTTP response to read from</param>
        /// <returns>The returned content from Cryptsy as a JToken. May be null
        /// if no return was provided.</returns>
        /// <exception cref="IOException">Where there was a problem reading the
        /// response from Cryptsy.</exception>
        /// <exception cref="CryptsyFailureException">Where Cryptsy returned an error.</exception>
        /// <exception cref="CryptsyResponseException">Where there was a problem
        /// parsing the response from Cryptsy.</exception>
        private async Task<JToken> GetReturnAsJToken(HttpResponseMessage response)
        {
            JObject jsonObj;

            using (Stream jsonStream = await response.Content.ReadAsStreamAsync())
            {
                using (StreamReader jsonStreamReader = new StreamReader(jsonStream))
                {
                    try
                    {
                        jsonObj = JObject.Parse(await jsonStreamReader.ReadToEndAsync());
                    }
                    catch (ArgumentException e)
                    {
                        throw new CryptsyResponseException("Could not parse response from Cryptsy.", e);
                    }
                }
            }

            if (null == jsonObj["success"])
            {
                throw new CryptsyResponseException("No success value returned in response from Cryptsy.");
            }

            if (!(jsonObj["success"].ToString().Equals("1")))
            {
                string errorMessage = jsonObj["error"].ToString();

                if (null == errorMessage)
                {
                    throw new CryptsyFailureException("Error response returned from Cryptsy.");
                }
                else
                {
                    throw new CryptsyFailureException("Error response returned from Cryptsy: "
                        + errorMessage);
                }
            }

            return jsonObj["return"];
        }

        private List<MarketOrder> ParseMarketOrders(OrderType orderType, JArray jArray)
        {
            List<MarketOrder> orders = new List<MarketOrder>(jArray.Count);

            try
            {
                foreach (JObject jsonOrder in jArray)
                {
                    Quantity quantity = Quantity.Parse(jsonOrder["quantity"]);
                    Quantity price;

                    switch (orderType)
                    {
                        case OrderType.Buy:
                            price = Quantity.Parse(jsonOrder["buyprice"]);
                            break;
                        case OrderType.Sell:
                            price = Quantity.Parse(jsonOrder["sellprice"]);
                            break;
                        default:
                            throw new ArgumentException("Unknown order type \"" + orderType.ToString() + "\".");
                    }

                    orders.Add(new MarketOrder(orderType, price, quantity));
                }
            }
            catch (System.FormatException e)
            {
                throw new CryptsyResponseException("Encountered invalid quantity/price while parsing market orders.", e);
            }

            return orders;
        }

        private List<MarketTrade<CryptsyMarketId, CryptsyTradeId>> ParseMarketTrades(JArray jsonTrades, CryptsyMarketId defaultMarketId)
        {
            List<MarketTrade<CryptsyMarketId, CryptsyTradeId>> trades = new List<MarketTrade<CryptsyMarketId, CryptsyTradeId>>();

            foreach (JObject jsonTrade in jsonTrades)
            {
                // FIXME: Need to correct timezone on this
                DateTime tradeDateTime = DateTime.Parse(jsonTrade["datetime"].ToString());
                JToken marketIdToken = jsonTrade["marketid"];
                CryptsyMarketId marketId = null == marketIdToken
                    ? defaultMarketId
                    : CryptsyMarketId.Parse(marketIdToken);
                CryptsyTradeId tradeId = CryptsyTradeId.Parse(jsonTrade["tradeid"]);
                OrderType tradeType = (OrderType)Enum.Parse(typeof(OrderType), jsonTrade["tradetype"].ToString());
                trades.Add(new MarketTrade<CryptsyMarketId, CryptsyTradeId>(tradeId,
                    tradeType, tradeDateTime,
                    Quantity.Parse(jsonTrade["tradeprice"]),
                    Quantity.Parse(jsonTrade["quantity"]), Quantity.Parse(jsonTrade["fee"]),
                    marketId
                ));
            }

            return trades;
        }

        private List<MyOrder<CryptsyMarketId, CryptsyOrderId>> ParseMyOrders(JArray jsonOrders, CryptsyMarketId defaultMarketId)
        {
            List<MyOrder<CryptsyMarketId, CryptsyOrderId>> orders = new List<MyOrder<CryptsyMarketId, CryptsyOrderId>>();

            foreach (JObject jsonTrade in jsonOrders)
            {
                // FIXME: Need to correct timezone on this
                DateTime created = DateTime.Parse(jsonTrade["created"].ToString());
                JToken marketIdToken = jsonTrade["marketid"];
                CryptsyMarketId marketId = null == marketIdToken
                    ? defaultMarketId
                    : CryptsyMarketId.Parse(marketIdToken);
                CryptsyOrderId orderId = CryptsyOrderId.Parse(jsonTrade["orderid"]);
                OrderType orderType = (OrderType)Enum.Parse(typeof(OrderType), jsonTrade["ordertype"].ToString());
                orders.Add(new MyOrder<CryptsyMarketId, CryptsyOrderId>(orderId,
                    orderType, created,
                    Quantity.Parse(jsonTrade["price"]),
                    Quantity.Parse(jsonTrade["quantity"]), Quantity.Parse(jsonTrade["orig_quantity"]),
                    marketId
                ));
            }

            return orders;
        }

        private List<MyTrade<CryptsyMarketId, CryptsyOrderId, CryptsyTradeId>> ParseMyTrades(JArray jsonTrades, CryptsyMarketId defaultMarketId)
        {
            List<MyTrade<CryptsyMarketId, CryptsyOrderId, CryptsyTradeId>> trades = new List<MyTrade<CryptsyMarketId, CryptsyOrderId, CryptsyTradeId>>();

            foreach (JObject jsonTrade in jsonTrades)
            {
                // FIXME: Need to correct timezone on this
                DateTime tradeDateTime = DateTime.Parse(jsonTrade["datetime"].ToString());
                JToken marketIdToken = jsonTrade["marketid"];
                CryptsyMarketId marketId = null == marketIdToken
                    ? defaultMarketId
                    : CryptsyMarketId.Parse(marketIdToken);
                CryptsyOrderId orderId = CryptsyOrderId.Parse(jsonTrade["order_id"]);
                CryptsyTradeId tradeId = CryptsyTradeId.Parse(jsonTrade["tradeid"]);
                OrderType tradeType = (OrderType)Enum.Parse(typeof(OrderType), jsonTrade["tradetype"].ToString());
                trades.Add(new MyTrade<CryptsyMarketId, CryptsyOrderId, CryptsyTradeId>(tradeId,
                    tradeType, tradeDateTime,
                    Quantity.Parse(jsonTrade["tradeprice"]),
                    Quantity.Parse(jsonTrade["quantity"]), Quantity.Parse(jsonTrade["fee"]),
                    marketId, orderId
                ));
            }

            return trades;
        }

        private List<Transaction> ParseTransactions(JArray jsonTransactions)
        {
            List<Transaction> transactions = new List<Transaction>();

            foreach (JObject jsonTransaction in jsonTransactions)
            {
                // FIXME: Need to correct timezone on this
                DateTime transactionPosted = DateTime.Parse(jsonTransaction["datetime"].ToString());
                TransactionType transactionType = (TransactionType)Enum.Parse(typeof(TransactionType), jsonTransaction["type"].ToString());

                Transaction transaction = new Transaction(jsonTransaction["currency"].ToString(),
                    transactionPosted, transactionType, 
                    Address.Parse(jsonTransaction["address"]), Quantity.Parse(jsonTransaction["amount"]),
                    Quantity.Parse(jsonTransaction["fee"]));
                transactions.Add(transaction);
            }

            return transactions;
        }

        public async Task<FormUrlEncodedContent> SignRequest(FormUrlEncodedContent request)
        {
            HMAC digester = new HMACSHA512(this.PrivateKey);
            StringBuilder hex = new StringBuilder();
            byte[] requestBytes = System.Text.Encoding.ASCII.GetBytes(await request.ReadAsStringAsync());

            request.Headers.Add(HEADER_SIGN, BitConverter.ToString(digester.ComputeHash(requestBytes)).Replace("-", "").ToLower());
            request.Headers.Add(HEADER_KEY, this.publicKey);

            return request;
        }
    }
}
