using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;
using Ploco.Models;

namespace Ploco.Behaviors
{
    public class LocomotiveDragBehavior : Behavior<FrameworkElement>
    {
        private Point _dragStartPoint;

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            AssociatedObject.PreviewMouseMove += OnPreviewMouseMove;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            AssociatedObject.PreviewMouseMove -= OnPreviewMouseMove;
            base.OnDetaching();
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            Point currentPos = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPos;

            if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (AssociatedObject is ListBox listBox && listBox.SelectedItem is LocomotiveModel modelList)
            {
                if (modelList.IsForecastGhost) return;
                DragDrop.DoDragDrop(AssociatedObject, modelList, DragDropEffects.Move);
            }
            else if (AssociatedObject?.DataContext is LocomotiveModel modelContext)
            {
                if (modelContext.IsForecastGhost) return;
                DragDrop.DoDragDrop(AssociatedObject, modelContext, DragDropEffects.Move);
            }
        }
    }
}
