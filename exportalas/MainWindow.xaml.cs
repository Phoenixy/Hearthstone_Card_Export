﻿using System;
using System.IO;
using System.Linq;
using System.Windows;
using HearthMirror;
using CsvHelper;
using System.Text;
using System.Globalization;

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
            //Reflection.Client. p = new Reflection();
            var status = Status.GetStatus().MirrorStatus;
            if (status == HearthMirror.Enums.MirrorStatus.Ok)
            {
                var signatureCollection = Reflection.Client.GetCollection().Where(x => x.PremiumType == 3);
                var premiumCollection = Reflection.Client.GetCollection().Where(x => x.PremiumType == 2);
                var goldenCollection = Reflection.Client.GetCollection().Where(x => x.PremiumType == 1);
                var commonCollection = Reflection.Client.GetCollection().Where(x => x.PremiumType == 0);

                //create CSV file
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
                        var amountPremium =
                            premiumCollection.Where(x => x.Id.Equals(dbCard.Key)).Select(x => x.Count).FirstOrDefault();
                        var amountSignature =
                            signatureCollection.Where(x => x.Id.Equals(dbCard.Key)).Select(x => x.Count).FirstOrDefault();
                        csv.WriteField(amountNormal);
                        csv.WriteField(amountGolden + amountPremium + amountSignature);

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
