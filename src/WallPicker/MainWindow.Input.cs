using System;
using System.Windows;
using System.Windows.Input;

namespace WallPicker;

/// <summary>Keyboard, mouse-wheel and search-overlay input for the picker.</summary>
public partial class MainWindow
{
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        // While the search box owns the keyboard, only Enter/Escape are global.
        if (SearchHost.Visibility == Visibility.Visible && SearchBox.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Enter)
            {
                HideSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ClearSearch();
                e.Handled = true;
            }

            return;
        }

        // Search overlay is visible but focus is elsewhere.
        if (SearchHost.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter)
            {
                HideSearch();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                ClearSearch();
                e.Handled = true;
                return;
            }
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                CloseWithAnimation();
                e.Handled = true;
                break;

            case Key.Left:
            case Key.H:
                Go(_selected - 1);
                e.Handled = true;
                break;

            case Key.Right:
            case Key.L:
                Go(_selected + 1);
                e.Handled = true;
                break;

            case Key.Up:
            case Key.K:
                Go(_selected - 10);
                e.Handled = true;
                break;

            case Key.Down:
            case Key.J:
                Go(_selected + 10);
                e.Handled = true;
                break;

            case Key.PageUp:
                Go(_selected - 25);
                e.Handled = true;
                break;

            case Key.PageDown:
                Go(_selected + 25);
                e.Handled = true;
                break;

            case Key.Home:
                Go(0);
                e.Handled = true;
                break;

            case Key.End:
                Go(_items.Count - 1);
                e.Handled = true;
                break;

            case Key.Enter:
                ApplyCurrent();
                e.Handled = true;
                break;

            case Key.OemQuestion:
            case Key.Divide:
                ShowSearch();
                e.Handled = true;
                break;

            case Key.F:
                ToggleFavorite();
                e.Handled = true;
                break;

            case Key.V:
                _favOnly = !_favOnly;
                ShowToast(_favOnly ? "Favorites only" : "All wallpapers");
                ApplyFilter();
                e.Handled = true;
                break;

            case Key.R:
                if (_items.Count > 0)
                {
                    Go(Random.Shared.Next(_items.Count));
                }

                e.Handled = true;
                break;

            case Key.M:
                CycleFit();
                e.Handled = true;
                break;

            case Key.O:
                ChooseFolder();
                e.Handled = true;
                break;

            case Key.E:
                ExportPalette();
                e.Handled = true;
                break;

            case Key.C:
                ShowCollectionOverlay();
                e.Handled = true;
                break;

            case Key.T:
                ShowTagOverlay();
                e.Handled = true;
                break;

            case Key.B:
                CycleCollectionView();
                e.Handled = true;
                break;
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        _wheelAccumulator += e.Delta;
        while (Math.Abs(_wheelAccumulator) >= 120)
        {
            int direction = _wheelAccumulator < 0 ? 1 : -1;
            Go(_selected + direction);
            _wheelAccumulator += _wheelAccumulator < 0 ? 120 : -120;
        }

        e.Handled = true;
    }

    private void ShowSearch()
    {
        SearchHost.Visibility = Visibility.Visible;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void HideSearch()
    {
        SearchHost.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void ClearSearch()
    {
        SearchBox.Text = string.Empty;
        SearchHost.Visibility = Visibility.Collapsed;
        Focus();
    }
}
