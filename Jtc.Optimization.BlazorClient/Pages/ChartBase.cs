using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Jtc.Optimization.Transformation;
using System.Diagnostics;
using Blazor.DynamicJavascriptRuntime.Evaluator;
using Jtc.Optimization.BlazorClient.Shared;
using Blazor.FileReader;
using Blazored.Toast.Services;
using Jtc.Optimization.Objects;
using System.Text.Json;
using CenterCLR.XorRandomGenerator;
using System.Text;

namespace Jtc.Optimization.BlazorClient
{
    public class ChartBase : ComponentBase
    {

        private const string ChartId = "Scatter";
        private static XorRandom _random = new XorRandom(42);
        public PlotlyData[] Config { get; set; }
        public int SampleRate { get; set; } = 1;
        public double? MinimumFitness { get; set; }
        public bool NewOnly { get; set; }
        public string ActivityLog { get { return _activityLogger.Status; } }
        private ActivityLogger _activityLogger { get; set; } = new ActivityLogger();
        public DateTime NewestTimestamp { get; set; }
        private List<int> _pickedColours;
        Queue<PlotlyData> _queue;
        private PlotlyBinder _binder;
        Stopwatch _stopWatch;
        protected WaitBase Wait { get; set; }
        [Inject]
        public IJSRuntime JSRuntime { get; set; }
        [Inject]
        public HttpClient HttpClient { get; set; }
        [Inject]
        public IFileReaderService FileReaderService { get; set; }
        protected ElementReference FileUpload { get; set; }
        [Inject]
        public IToastService ToastService { get; set; }
        //todo: config
        protected bool EnableServerData { get; set; }

        protected bool HasChartData { get { return GetHasChartData(); } }

        private bool GetHasChartData()
        {
            dynamic context = new EvalContext(JSRuntime);
            (context as EvalContext).Expression = () => context.ClientStorage.hasChartData();
            return (context as EvalContext).Invoke<bool>();
        }

        public ChartBase()
        {
            _pickedColours = new List<int>();
            _stopWatch = new Stopwatch();
            _binder = new PlotlyBinder();
        }

        protected async override Task OnInitializedAsync()
        {

            Program.HttpClient = HttpClient;
            Program.JsRuntime = JSRuntime;


            using (dynamic context = new EvalContext(JSRuntime))
            {
                (context as EvalContext).Expression = () => context.jQuery("body").css("overflow-y", "hidden");
            }

            await base.OnInitializedAsync();
        }

        protected override void OnAfterRender(bool firstRender)
        {

            try
            {
                base.OnAfterRender(firstRender);
            }
            catch (Exception ex)
            {
                ShowError(ex, "Error occurred setting up chart.");
            }
        }

        protected async Task UpdateChart()
        {
            await Wait.Show();

            try
            {
                _stopWatch.Start();
                await BindRemote();
                //todo: reinstate new only
                if (NewOnly)
                {
                    throw new NotImplementedException();
                }
                else
                {
                    var settings = new EvalContextSettings();
                    settings.SerializableTypes.Add(typeof(PlotlyData[]));
                    using (dynamic context = new EvalContext(JSRuntime))
                    {
                        (context as EvalContext).Expression = () => context.PlotlyInterop.newPlot(Config);
                    }
                }
            }
            finally
            {
                await Wait.Hide();
            }
        }

        protected async Task GetStoredChart()
        {
            _stopWatch.Start();
            await Wait.Show();
            StateHasChanged();
            //todo: reinstate new only
            //if (!NewOnly)
            //{
            //    Config.Data.Datasets.Clear();
            //    await JsRuntime.SetupChart(Config);
            //}

            _binder = new PlotlyBinder();

            try
            {
                dynamic context = new EvalContext(JSRuntime);
                (context as EvalContext).Expression = () => context.ClientStorage.fetchChartData();
                var log = await (context as EvalContext).InvokeAsync<string>();

                using (var reader = new SwitchReader(new StringReader(log)))
                {
                    var data = await _binder.Read(reader, SampleRate == 0 ? 1 : SampleRate, false, NewOnly ? NewestTimestamp : DateTime.MinValue, minimumFitness: MinimumFitness);
                    await ShowChart(data);
                }

            }
            catch (Exception ex)
            {
                ShowError(ex, "An error occurred attempting to bind.");
            }
            finally
            {
                await Wait.Hide();
            }
        }

        private async Task BindRemote()
        {
            if (!NewOnly)
            {
                _binder = new PlotlyBinder();
                _pickedColours.Clear();
            }

            using (var file = new SwitchReader(new StreamReader((await HttpClient.GetStreamAsync($"http://localhost:5000/api/data")))))
            {
                var data = await _binder.Read(file, SampleRate == 0 ? 1 : SampleRate, false, NewOnly ? NewestTimestamp : DateTime.MinValue);
                ShowChart(data);
            }
        }

        private async Task BindRemoteOnServer()
        {
            if (!NewOnly)
            {
                _pickedColours.Clear();
            }

            using (var file = new StreamReader((await HttpClient.GetStreamAsync($"http://localhost:5000/api/data/Sample/{(SampleRate == 0 ? 1 : SampleRate)}"))))
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, PlotlyData>>(file.ReadToEnd());
                ShowChart(data);
            }
        }

        private async Task ShowChart(Dictionary<string, PlotlyData> data)
        {

            Config = data.Values.ToArray();

            //only serializes first member
            //JsRuntime.InvokeVoidAsync("PlotlyInterop.newPlot", Config);

            foreach (var item in data)
            {
                item.Value.Marker = new Marker { Color = PickColourName() };
            }

            data.Last().Value.Marker.Color = "red";

            var settings = new EvalContextSettings { EnableDebugLogging = true };
            settings.SerializableTypes.Add(Config.GetType());
            settings.JsonSerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IgnoreNullValues = true };
            using (dynamic context = new EvalContext(JSRuntime, settings))
            {
                (context as EvalContext).Expression = () => context.PlotlyInterop.newPlot(Config);
                await (context as EvalContext).InvokeAsync<dynamic>();
            }

            _pickedColours.Clear();

            NewestTimestamp = DateTime.Parse(Config.Last().X.Last());
            _activityLogger.Add($"Newest Timestamp: ", NewestTimestamp);
            _activityLogger.Add("Exection Time:", _stopWatch.Elapsed);
            _activityLogger.Add($"Updated Rows: ", Config.Last().X.Count);
            _stopWatch.Stop();
        }

        private string PickRandomColour()
        {
            var colour = "#";
            for (int i = 0; i < 3; i++)
            {
                colour += (char)_random.Next('a', 'f');
            }

            return colour;
        }

        private string PickColourName()
        {
            var names = new[] { "Yellow", "Olive", "Lime", "Aqua", "Teal", "Blue", "Fuchsia", "Purple" };
            if (_pickedColours.Count == names.Length)
            {
                return PickRandomColour();
            }
            var picked = _random.Next(0, names.Length - 1);

            while (_pickedColours.Contains(picked))
            {
                picked = _random.Next(0, names.Length - 1);
            }

            _pickedColours.Add(picked);
            return names[picked];
        }

        protected async Task UploadFile()
        {
            _stopWatch.Start();
            await Wait.Show();
            //todo: reinstate new only
            //if (!NewOnly)
            //{
            //    Config.Data.Datasets.Clear();
            //    await JsRuntime.SetupChart(Config);
            //}

            _binder = new PlotlyBinder();
            try
            {
                var fileReader = FileReaderService.CreateReference(FileUpload);
                foreach (var file in await fileReader.EnumerateFilesAsync())
                {
                    using (Stream stream = await file.OpenReadAsync())
                    {
                        using (var streamReader = new StreamReader(stream))
                        {
                            using (var reader = new SwitchReader(streamReader))
                            {
                                //wait.ProgressPercent = (int)(stream.Length / (stream.Position+1))*100;
                                var data = await _binder.Read(reader, SampleRate == 0 ? 1 : SampleRate, false, NewOnly ? NewestTimestamp : DateTime.MinValue, minimumFitness: MinimumFitness);
                                await ShowChart(data);
                                //stream.Position = 0;
                                //await JSRuntime.InvokeVoidAsync("ClientStorage.storeChartData", await streamReader.ReadToEndAsync());
                            }
                            //ToastService.ShowSuccess("Chart data was stored.");
                        }
                    }
                }

                await fileReader.ClearValue();
            }
            catch (Exception ex)
            {
                ShowError(ex, "An error occurred attempting to bind.");
            }
            finally
            {
                await Wait.Hide();
            }
        }

        private void ShowError(Exception ex, string message)
        {
#if DEBUG
            Console.WriteLine(ex.ToString());
#endif
            ToastService.ShowError(message);
        }

    }
}
