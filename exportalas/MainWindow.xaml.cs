using System;
using System.IO;
using System.Linq;
using System.Windows;
using HearthMirror;
using CsvHelper;
using System.Text;
using System.Globalization;
using Microsoft.Win32; // Added for SaveFileDialog
using System.Threading.Tasks; // Added for Task.Run
using System.Collections.Generic; // Added for Dictionary

namespace exportalas
{
    // Helper class to store aggregated card counts
    public class CardCountsForExport
    {
        public int Normal { get; set; }
        public int Golden { get; set; }
        public int Premium { get; set; } // For cards like Diamond (PremiumType 2)
        public int Signature { get; set; } // PremiumType 3
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {

        public Thickness TitleBarMargin
        {
            get { return new Thickness(0, TitlebarHeight, 0, 0); }
        }

        public static void Refresh()
        {
            if (Application.Current.Windows.OfType<MainWindow>().Any())
            {
                _ = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            }
        }

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                Title = "Gyűjtemény exportálás";
            }
            catch (Exception e)
            {
                _ = MessageBox.Show(e.Message, "Gyűjtemény exportálás", MessageBoxButton.OK);
            }
        }

        private void MainWindow_OnContentRendered(object sender, EventArgs e)
        {
            this.SizeToContent = SizeToContent.Manual;
        }

        private void MainWindow_OnClosed(object sender, EventArgs e)
        {
        }

        private async void export_click(object sender, RoutedEventArgs e)
        {
            export_bt.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    try // General try-catch for the entire Task.Run body
                    {
                        var status = Status.GetStatus().MirrorStatus;
                        if (status == HearthMirror.Enums.MirrorStatus.Ok)
                        {
                            var fullCollection = Reflection.Client.GetCollection();
                            var aggregatedCardCounts = new Dictionary<string, CardCountsForExport>();

                            if (fullCollection != null)
                            {
                                foreach (var cardInCollection in fullCollection)
                                {
                                    if (!aggregatedCardCounts.TryGetValue(cardInCollection.Id, out var counts))
                                    {
                                        counts = new CardCountsForExport();
                                        aggregatedCardCounts[cardInCollection.Id] = counts;
                                    }
                                    switch (cardInCollection.PremiumType)
                                    {
                                        case 0: counts.Normal += cardInCollection.Count; break;
                                        case 1: counts.Golden += cardInCollection.Count; break;
                                        case 2: counts.Premium += cardInCollection.Count; break;
                                        case 3: counts.Signature += cardInCollection.Count; break;
                                    }
                                }
                            }

                            SaveFileDialog saveFileDialog = new SaveFileDialog();
                            saveFileDialog.Filter = "CSV fájl (*.csv)|*.csv";
                            saveFileDialog.DefaultExt = ".csv";
                            saveFileDialog.FileName = "gyujtemeny.csv";
                            saveFileDialog.Title = "Gyűjtemény exportálása";

                            bool? dialogResult = null;
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                dialogResult = saveFileDialog.ShowDialog();
                            });

                            if (dialogResult == true)
                            {
                                string filePath = saveFileDialog.FileName;
                                try // Try-catch for file I/O
                                {
                                    using (var textWriter = new StreamWriter(filePath, false, Encoding.UTF8))
                                    {
                                        var csv = new CsvWriter(textWriter, CultureInfo.InvariantCulture);
                                        csv.WriteField("Id");
                                        csv.WriteField("Név");
                                        csv.WriteField("Normal lapok");
                                        csv.WriteField("Golden lapok");
                                        csv.NextRecord();

                                        foreach (var dbCard in HearthDb.Cards.Collectible)
                                        {
                                            csv.WriteField(dbCard.Value.DbfId);
                                            csv.WriteField(dbCard.Value.Name);
                                            if (aggregatedCardCounts.TryGetValue(dbCard.Key, out var counts))
                                            {
                                                csv.WriteField(counts.Normal);
                                                // "Golden lapok" column includes Golden, Diamond (PremiumType 2), and Signature (PremiumType 3) cards.
                                                csv.WriteField(counts.Golden + counts.Premium + counts.Signature);
                                            }
                                            else
                                            {
                                                csv.WriteField(0);
                                                csv.WriteField(0);
                                            }
                                            csv.NextRecord();
                                        }
                                    }
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        MessageBox.Show($"Sikeres exportálás! A {Path.GetFileName(filePath)} fájl létrejött!", "Sikeres");
                                    });
                                }
                                catch (IOException ioEx)
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        MessageBox.Show($"Hiba történt a fájl írása közben: {ioEx.Message}", "Fájl írási hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                                    });
                                }
                            }
                        }
                        else if (status == HearthMirror.Enums.MirrorStatus.ProcNotFound)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show("Nem fut a Hearthstone!", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                        else if (status == HearthMirror.Enums.MirrorStatus.Error)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show("Hiba történt a Hearthstone adatok lekérése közben.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                    }
                    catch (Exception ex) // General catch for unexpected errors within Task.Run
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show($"Ismeretlen hiba történt az exportálás során: {ex.Message}", "Exportálási hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
                    }
                });
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    export_bt.IsEnabled = true;
                });
            }
        }
    }
}
