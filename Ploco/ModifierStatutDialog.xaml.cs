using System;
using System.Windows;
using System.Windows.Controls;
using Ploco.Models;
using Ploco.Helpers;

namespace Ploco
{
    public partial class ModifierStatutDialog : Window
    {
        // Ajoute ce champ pour stocker l'instance de locomotive
        private Locomotive loco;

        public StatutLocomotive NewStatut { get; private set; }

        public ModifierStatutDialog(Locomotive loco)
        {
            InitializeComponent();
            Owner = Application.Current.MainWindow; // Définit la fenêtre principale comme propriétaire
            WindowStartupLocation = WindowStartupLocation.CenterOwner; // Centre la fenêtre sur la principale
            // Assure-toi d'assigner l'instance au champ
            this.loco = loco;
            tbLocoInfo.Text = loco.ToString();
            // Pré-remplissage des champs date/heure avec la date/heure actuelles.
            tbEMDateTime.Text = DateTime.Now.ToString("G");
            tbATEVAPDateTime.Text = DateTime.Now.ToString("G");

            // Sélection par défaut dans le ComboBox en fonction du statut actuel.
            cbStatut.SelectedIndex = (int)loco.Statut;
        }

        private void btnEMModifier_Click(object sender, RoutedEventArgs e)
        {
            tbEMDateTime.IsReadOnly = !tbEMDateTime.IsReadOnly;
            if (!tbEMDateTime.IsReadOnly)
            {
                tbEMDateTime.Focus();
                tbEMDateTime.SelectAll();
            }
        }

        private void btnATEVAPModifier_Click(object sender, RoutedEventArgs e)
        {
            tbATEVAPDateTime.IsReadOnly = !tbATEVAPDateTime.IsReadOnly;
            if (!tbATEVAPDateTime.IsReadOnly)
            {
                tbATEVAPDateTime.Focus();
                tbATEVAPDateTime.SelectAll();
            }
        }

        private void btnValider_Click(object sender, RoutedEventArgs e)
        {
            // Récupérer le nouveau statut depuis le ComboBox.
            if (cbStatut.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string statutString && !string.IsNullOrWhiteSpace(statutString))
            {
                if (Enum.TryParse(statutString, out StatutLocomotive statut))
                {
                    NewStatut = statut;
                    // Mettre à jour le statut de la locomotive afin que le binding se rafraîchisse
                    loco.Statut = NewStatut;
                }
            }

            // Préparer les infos pour "Défaut moteur"
            string defautMoteur = "";
            if (chkDefautMoteur.IsChecked == true)
            {
                defautMoteur = "Défaut moteur: ";
                if (chk75.IsChecked == true) defautMoteur += "75% ";
                if (chk50.IsChecked == true) defautMoteur += "50% ";
                if (chkPRP1.IsChecked == true) defautMoteur += "PRP1 ";
                if (chkPRP2.IsChecked == true) defautMoteur += "PRP2 ";
                if (chkCVS1.IsChecked == true) defautMoteur += "CVS1 ";
                if (chkCVS2.IsChecked == true) defautMoteur += "CVS2 ";
            }

            // Préparer les infos pour EM
            string emInfo = "";
            if (chkEM.IsChecked == true)
            {
                emInfo = $"EM: Date/Heure={tbEMDateTime.Text}, Note={tbEMNote.Text}";
            }

            // Préparer les infos pour ATE/VAP
            string atevapInfo = "";
            if (chkATEVAP.IsChecked == true)
            {
                atevapInfo = $"ATE/VAP: Date/Heure={tbATEVAPDateTime.Text}, Note={tbATEVAPNote.Text}";
            }

            string notesLibres = tbNotesLibres.Text;

            // Mettre à jour la locomotive avec ces informations
            loco.DefautMoteurDetails = defautMoteur;
            loco.EMDetails = emInfo;
            loco.ATEVAPDetails = atevapInfo;
            loco.ModificationNotes = notesLibres;
            loco.LastModificationDate = DateTime.Now;

            // (Optionnel) Créer une entrée d'archive dans un fichier log, si vous le souhaitez toujours.
            string archiveEntry = $"Action: Modifier Statut, Loco: {tbLocoInfo.Text}, Nouveau Statut: {NewStatut}, {defautMoteur}, {emInfo}, {atevapInfo}, Notes: {notesLibres}, Créé le: {DateTime.Now:G}";
            try
            {
                System.IO.File.AppendAllText("StatutModificationLog.txt", archiveEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.Error("Erreur lors de l'enregistrement de l'archive via fichier texte", ex, "ModifierStatutDialog");
                MessageBox.Show("Erreur lors de l'enregistrement de l'archive : " + ex.Message);
            }

            this.DialogResult = true;
            this.Close();
        }

        private void btnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void chkDefautMoteur_Checked(object sender, RoutedEventArgs e)
        {
            spDefautMoteurDetails.Visibility = Visibility.Visible;
        }

        private void chkDefautMoteur_Unchecked(object sender, RoutedEventArgs e)
        {
            spDefautMoteurDetails.Visibility = Visibility.Collapsed;
        }

        private void chkEM_Checked(object sender, RoutedEventArgs e)
        {
            spEMDetails.Visibility = Visibility.Visible;
        }

        private void chkEM_Unchecked(object sender, RoutedEventArgs e)
        {
            spEMDetails.Visibility = Visibility.Collapsed;
        }

        private void chkATEVAP_Checked(object sender, RoutedEventArgs e)
        {
            spATEVAPDetails.Visibility = Visibility.Visible;
        }

        private void chkATEVAP_Unchecked(object sender, RoutedEventArgs e)
        {
            spATEVAPDetails.Visibility = Visibility.Collapsed;
        }
    }
}
