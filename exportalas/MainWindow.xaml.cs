using System;
using System.IO;
using System.Linq;
using System.Windows;
using HearthMirror;
using CsvHelper;
using System.Text;
using System.Globalization;
using System.Runtime;

namespace exportalas
{
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

        private void export_click(object sender, RoutedEventArgs e)
        {
            var status = Status.GetStatus().MirrorStatus;
            if (status == HearthMirror.Enums.MirrorStatus.Ok)
            {
                var goldenCollection = Reflection.GetCollection().Where(x => x.Premium);
                var commonCollection = Reflection.GetCollection().Where(x => !x.Premium);

                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "gyujtemeny.csv");

                using (var textWriter = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    export_bt.IsEnabled = false;
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

                        var amountNormal =
                            commonCollection.Where(x => x.Id.Equals(dbCard.Key)).Select(x => x.Count).FirstOrDefault();
                        var amountGolden =
                            goldenCollection.Where(x => x.Id.Equals(dbCard.Key)).Select(x => x.Count).FirstOrDefault();
                        csv.WriteField(amountNormal);
                        csv.WriteField(amountGolden);

                        csv.NextRecord();
                    }
                }
                export_bt.IsEnabled = true;
                MessageBox.Show("Sikeres exportálás! A gyujtemeny.csv fájl létrejött az asztalon!", "Sikeres");
            }
            else if (status == HearthMirror.Enums.MirrorStatus.ProcNotFound)
            {
                MessageBox.Show("Nem fut a Hearthstone!", "Hiba");
            }
            else if (status == HearthMirror.Enums.MirrorStatus.Error)
            {
                MessageBox.Show("Nem fut a Hearthstone!", "Egyéb hiba");
            }
        }

        private void ListBoxItem_Selected(object sender, RoutedEventArgs e)
        {

        }
    }
}
