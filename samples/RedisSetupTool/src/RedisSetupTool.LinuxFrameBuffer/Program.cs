using CodeBrix.Platform.UI.Hosting;
using CodeBrix.Platform.UI.Runtime.Skia;
using System;
using Windows.Graphics.Display;

namespace RedisSetupTool;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = CodeBrixPlatformHostBuilder.Create()
            .App(() => new App())
            //This head has no window manager and no OS chrome, so the two things the
            //  application needs from the outside world are supplied here: a keyboard for its
            //  many text fields and its terminals, and a file picker for the Dockerfile the
            //  image linter reads.
            .UseLinuxFrameBuffer(fb => fb
                .Orientation(DisplayOrientations.Landscape, isPreferredOrientation: true)
                .AutoRotationEnabled(true)
                .EnableFileOpenPicker(new FilePickerOptions
                {
                    AllowMultipleFileSelect = false,
                    StartFolder = "/home",
                })
                .EnableSoftwareKeyboard(new SoftwareKeyboardOptions
                {
                    ShowDismissKey = true, //default behavior = true
                    KeyHeight = SoftwareKeyHeight.PortraitHalfLandscapeHalf,
                })
            )
            .UseDirectSkiaCanvasMode() //Experimental - should be safe to leave enabled
            .Build();

        host.Run();
    }
}
