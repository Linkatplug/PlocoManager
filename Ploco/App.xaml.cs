using System;
using System.IO;
using System.Windows;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Ploco.Data;
using Ploco.ViewModels;
using Ploco.Dialogs;

namespace Ploco
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ploco", "Logs", "ploco_.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
                .CreateLogger();

            Log.Information("=== Application Ploco Démarrée ===");

            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            // Création et affichage du Splash Screen
            var splash = new SplashWindow();
            this.MainWindow = splash;
            splash.Show();

            // Démarrage de l'initialisation en arrière-plan
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // Forcer l'initialisation sur le thread UI pour les composants WPF
                    MainWindow? mainWindow = null;
                    await Current.Dispatcher.InvokeAsync(() =>
                    {
                        mainWindow = new MainWindow();
                    });

                    if (mainWindow != null)
                    {
                        await mainWindow.InitializeAppAsync(splash);

                        await Current.Dispatcher.InvokeAsync(() =>
                        {
                            this.MainWindow = mainWindow;
                            mainWindow.Show();
                            splash.Close();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Erreur fatale lors de l'initialisation depuis le Splash Screen.");
                    Environment.Exit(1);
                }
            });
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IPlocoRepository>(provider => new PlocoRepository("ploco.db"));
            services.AddSingleton<IDialogService, DialogService>();
            services.AddTransient<MainViewModel>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("=== Application Ploco Arrêtée ===");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
