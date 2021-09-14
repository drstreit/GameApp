using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.IO;
using System.Threading;
using System.Configuration;
using System.Collections.Concurrent;
using System.Diagnostics;
using ALogger;

namespace InsertTextIntoGSheet
{
    public class GoogleSheet
    {
        private static readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };
        private SheetsService service;
        private readonly ConcurrentQueue<TradeResult> GSheetItemQueue = new();
        private readonly Logger _log;

        public GoogleSheet(Logger log)
        {
            _log = log;
            SetGoogleService();
        }

        private void SetGoogleService()
        {
            // The file token.json stores the user's access and refresh tokens, and is created
            // automatically when the authorization flow completes for the first time
            UserCredential credential;
            using (var stream =
                new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                // The file token.json stores the user's access and refresh tokens, and is created
                // automatically when the authorization flow completes for the first time.
                string credPath = "token.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
            }
            // Create Google Sheets API service.
            service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ConfigurationManager.AppSettings["TradeManSheet"]
            ,
            });
        }

        public bool CheckSheet()
        {
            var ssRequest = service.Spreadsheets.Get(ConfigurationManager.AppSettings["TradeManSheet"]);
            Spreadsheet ss = ssRequest.Execute();

            IEnumerable<Sheet> query = ss.Sheets.Where(sheet => sheet.Properties.Title.Equals(ConfigurationManager.AppSettings["SheetName"]));
            if (query.Count() == 1)
                return true;
            else

                return false;
        }

        public bool AppendValues(ConcurrentQueue<KeyValuePair<string, double>> queue)
        {
            var range = $"{ConfigurationManager.AppSettings["SheetName"]}!A:C";
            //SpreadsheetsResource.ValuesResource.GetRequest requestRead =
            //       service.Spreadsheets.Values.Get(ConfigurationManager.AppSettings["TradeManSheet"], range);
            //int numberOfRows = requestRead.Execute().Values.Count + 1;

            // define formalas
            //var ColC_Hour = $"=hour($B{numberOfRows}-$A{numberOfRows})";
            IList<IList<object>> values = new List<IList<object>>();
            foreach (var item in queue)
            {
                IList<object> obj = new List<object>()
                {
                    item.Key,
                    item.Value,
                    $"{DateTime.Now.Month}/{DateTime.Now.Day}/{DateTime.Now.Year} {DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}" 
                };
                values.Add(obj);
            }

            SpreadsheetsResource.ValuesResource.AppendRequest request =
                    service.Spreadsheets.Values.Append(new ValueRange() { Values = values }, ConfigurationManager.AppSettings["TradeManSheet"], range);

            request.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
            request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
            var response = request.Execute();
            if (response.Updates.UpdatedRows == values.Count)
                return true;
            else
                return false;
        }

        public void GetPrices()
        {
            long action(object obj)
            {
                var range = $"{obj}!A:C";
                long h = 0;
                SpreadsheetsResource.ValuesResource.GetRequest requestRead =
                       service.Spreadsheets.Values.Get(ConfigurationManager.AppSettings["TradeManSheet"], range);
                var items = requestRead.Execute().Values;
                _log.AddMessage(new LogMessage(Levels.Log, $"Received {items.Count} lines items from '{obj}'"));
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Count == 3 && double.TryParse((string)items[i][2], out double d))
                    {
                        GSheetItemQueue.Enqueue(new()
                        {
                            Price = d,
                            Name = items[i][1].ToString(),
                            Tier = items[i][0].ToString(),
                            PriceCell = $"{obj}!C{i+1}",
                        });
                        h++;
                    }
                }
                _log.AddMessage(new LogMessage(Levels.Success, $"Pushed {h} trade items from '{obj}' to queue"));
                return items.Count;
            }

            string[] sheets = new string[] { "Weapons", "Tools", "Apparel", "Resources", "Consumables", "Ammo" };
            var tasks = new List<Task<long>>();

            foreach (var sheet in sheets)
            {
                tasks.Add(Task<long>.Factory.StartNew(action, sheet));
            }
            // wait for finish
            try
            {
                // Wait for all the tasks to finish.
                Task.WaitAll(tasks.ToArray());
            }
            catch (AggregateException e)
            {
                for (int j = 0; j < e.InnerExceptions.Count; j++)
                {
                    _log.AddMessage(new LogMessage(Levels.Error, "GetPrices error: " + e.InnerExceptions[j]));
                    Debug.WriteLine(e.InnerExceptions[j]);  
                }
            }          
        }

        public void DeletePrices()
        {
            // take GSheetItemQueue and set all prices to zero
            throw new NotImplementedException();
        }
        public void UpdatePrices()
        {
            // take GSheetItemQueue and update all prices
            throw new NotImplementedException();
        }
    }
}
