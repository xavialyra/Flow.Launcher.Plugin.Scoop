using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public class IconHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);


    public static ImageSource? GetIconAsImageSource(string exeFilePath, int iconIndex = 0)
    {
        var iconHandle = ExtractIcon(IntPtr.Zero, exeFilePath, iconIndex);
        if (iconHandle == IntPtr.Zero)
        {
            return null;
        }

        return DispatcherHelper.InvokeOnUIThread(() =>
        {
            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                return bitmapSource;
            }
            finally
            {
                DestroyIcon(iconHandle);
            }
        });
    }

    public static ImageSource? GetIconAsPath(string iconPath)
    {
        if (!File.Exists(iconPath))
        {
            return null;
        }

        return DispatcherHelper.InvokeOnUIThread(() =>
        {
            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(iconPath, UriKind.RelativeOrAbsolute);
                bitmapImage.EndInit();
                return bitmapImage;
            }
            catch
            {
                return null;
            }
        });
    }
}

public static class DispatcherHelper
{
    public static void InvokeOnUIThread(Action action)
    {
        if (Application.Current == null)
        {
            throw new InvalidOperationException(
                "Application.Current is null");
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }


    public static T InvokeOnUIThread<T>(Func<T> func)
    {
        if (Application.Current == null)
        {
            throw new InvalidOperationException(
                "Application.Current is null");
        }

        return Application.Current.Dispatcher.CheckAccess() ? func() : Application.Current.Dispatcher.Invoke(func);
    }
}