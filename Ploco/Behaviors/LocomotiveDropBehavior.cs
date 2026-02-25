using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;
using Ploco.Helpers;
using Ploco.Models;

namespace Ploco.Behaviors
{
    public class LocomotiveDropBehavior : Behavior<FrameworkElement>
    {
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register("Command", typeof(ICommand), typeof(LocomotiveDropBehavior), new PropertyMetadata(null));

        public ICommand Command
        {
            get => (ICommand)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public static readonly DependencyProperty IsRollingLineRowProperty =
            DependencyProperty.Register("IsRollingLineRow", typeof(bool), typeof(LocomotiveDropBehavior), new PropertyMetadata(false));

        public bool IsRollingLineRow
        {
            get => (bool)GetValue(IsRollingLineRowProperty);
            set => SetValue(IsRollingLineRowProperty, value);
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.AllowDrop = true;
            AssociatedObject.Drop += OnDrop;
            AssociatedObject.DragOver += OnDragOver;
            AssociatedObject.DragLeave += OnDragLeave;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.Drop -= OnDrop;
            AssociatedObject.DragOver -= OnDragOver;
            AssociatedObject.DragLeave -= OnDragLeave;
            base.OnDetaching();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(LocomotiveModel)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (IsRollingLineRow && AssociatedObject is Border border)
            {
                var track = border.DataContext as TrackModel;
                var loco = (LocomotiveModel)e.Data.GetData(typeof(LocomotiveModel));
                if (track != null && loco != null)
                {
                    bool canDrop = !track.Locomotives.Any() || track.Locomotives.Contains(loco) ||
                                   (track.Locomotives.Count == 1 && !track.Locomotives.Contains(loco));
                    e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
                    border.Background = canDrop ? new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)) : Brushes.MistyRose;
                }
            }
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            if (IsRollingLineRow && AssociatedObject is Border border)
            {
                border.Background = Brushes.Transparent;
            }
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (IsRollingLineRow && AssociatedObject is Border borderVisual)
            {
                borderVisual.Background = Brushes.Transparent;
            }

            if (!e.Data.GetDataPresent(typeof(LocomotiveModel)))
                return;

            var loco = (LocomotiveModel)e.Data.GetData(typeof(LocomotiveModel));
            var target = AssociatedObject.DataContext; 

            // Calculate InsertIndex if ListBox
            int insertIndex = -1;
            if (AssociatedObject is ListBox listBox)
            {
                insertIndex = GetInsertIndex(listBox, e.GetPosition(listBox));
            }

            if (Command != null && Command.CanExecute(null))
            {
                var args = new LocomotiveDropArgs
                {
                    Loco = loco,
                    Target = target,
                    InsertIndex = insertIndex,
                    DropPosition = e.GetPosition(AssociatedObject),
                    TargetActualWidth = AssociatedObject.ActualWidth,
                    IsRollingLineRow = IsRollingLineRow
                };
                Command.Execute(args);
                e.Handled = true;
            }
        }

        private static int GetInsertIndex(ListBox listBox, Point dropPosition)
        {
            var element = listBox.InputHitTest(dropPosition) as DependencyObject;
            while (element != null && element is not ListBoxItem)
            {
                element = VisualTreeHelper.GetParent(element);
            }

            if (element is ListBoxItem item)
            {
                return listBox.ItemContainerGenerator.IndexFromContainer(item);
            }

            return listBox.Items.Count;
        }
    }
}
