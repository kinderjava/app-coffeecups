//#define AUTH

using System;
using Microsoft.WindowsAzure.MobileServices;
using Microsoft.WindowsAzure.MobileServices.Sync;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.WindowsAzure.MobileServices.SQLiteStore;
using System.Diagnostics;
using Xamarin.Forms;
using CoffeeCups.Helpers;
using CoffeeCups.Authentication;
using CoffeeCups;
using System.IO;
using Plugin.Connectivity;



[assembly: Dependency(typeof(AzureService))]
namespace CoffeeCups
{
    public class AzureService
    {
        public MobileServiceClient Client { get; set; } = null;
        IMobileServiceSyncTable<CupOfCoffee> coffeeTable;
        public static bool UseAuth { get; set; } = false;

 
        public async Task Initialize()
        {
            if (Client?.SyncContext?.IsInitialized ?? false)
                return;

            var appUrl = "https://YOUR-URL.azurewebsites.net";

#if AUTH      
            Client = new MobileServiceClient(appUrl, new AuthHandler());

            if (!string.IsNullOrWhiteSpace (Settings.AuthToken) && !string.IsNullOrWhiteSpace (Settings.UserId)) {
                Client.CurrentUser = new MobileServiceUser (Settings.UserId);
                Client.CurrentUser.MobileServiceAuthenticationToken = Settings.AuthToken;
            }
#else
            //Create our client
            Client = new MobileServiceClient(appUrl);
#endif

            //InitialzeDatabase for path
            var path = InitializeDatabase();

            //setup our local sqlite store and intialize our table
            var store = new MobileServiceSQLiteStore(path);

            //Define table
            store.DefineTable<CupOfCoffee>();

            //Initialize SyncContext
            await Client.SyncContext.InitializeAsync(store, new MobileServiceSyncHandler());

            //Get our sync table that will call out to azure
            coffeeTable = Client.GetSyncTable<CupOfCoffee>();
        }

        private string InitializeDatabase()
        {
#if __ANDROID__ || __IOS__
            Microsoft.WindowsAzure.MobileServices.CurrentPlatform.Init();
#endif
            SQLitePCL.Batteries.Init();

            var path =  "syncstore2.db";

#if __ANDROID__
            path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), path);

            if (!File.Exists(path))
            {
                File.Create(path).Dispose();
            }
#endif

            return path;
        }



        public async Task<IEnumerable<CupOfCoffee>> GetCoffees()
        {
            await Initialize();
            await SyncCoffee();
            return await coffeeTable.OrderBy(c => c.DateUtc).ToEnumerableAsync();
        }

        public async Task<CupOfCoffee> AddCoffee(bool atHome)
        {
            await Initialize();
            
            //create and insert coffee
            var coffee = new CupOfCoffee
            {
                DateUtc = DateTime.UtcNow,
                MadeAtHome = atHome,
                OS = Device.OS.ToString()
            };

            await coffeeTable.InsertAsync(coffee);

            //Synchronize coffee
            await SyncCoffee();

            return coffee;
        }

        public async Task SyncCoffee()
        {
            try
            {
                if (!CrossConnectivity.Current.IsConnected)
                    return;

                //pull down all latest changes and then push current coffees up
                await Client.SyncContext.PushAsync();
                await coffeeTable.PullAsync("allCoffees", coffeeTable.CreateQuery());
            }
            catch(Exception ex)
            {
                Debug.WriteLine("Unable to sync coffees, that is alright as we have offline capabilities: " + ex);
            }
            
        }

        public async Task<bool> LoginAsync()
        {

            await Initialize();

            var auth = DependencyService.Get<IAuthentication>();
            var user = await auth.LoginAsync(Client, MobileServiceAuthenticationProvider.Twitter);

            if (user == null)
            {
                Settings.AuthToken = string.Empty;
                Settings.UserId = string.Empty;
                Device.BeginInvokeOnMainThread(async () =>
                {
                    await App.Current.MainPage.DisplayAlert("Login Error", "Unable to login, please try again", "OK");
                });
                return false;
            }
            else
            {
                Settings.AuthToken = user.MobileServiceAuthenticationToken;
                Settings.UserId = user.UserId;
            }

            return true;
        }
    }
}
    
