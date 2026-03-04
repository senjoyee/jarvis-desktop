using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public class Program
{
    public static void Main()
    {
        int size = 256;
        using (Bitmap bmp = new Bitmap(size, size))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Background gradient
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(10, 10, size - 20, size - 20);
                    using (PathGradientBrush pgb = new PathGradientBrush(path))
                    {
                        pgb.CenterColor = Color.FromArgb(255, 0, 255, 204); // Vibrant magenta/cyan
                        pgb.SurroundColors = new Color[] { Color.FromArgb(255, 0, 102, 255) }; // Vibrant blue
                        g.FillPath(pgb, path);
                    }
                }

                // Inner circle
                using (SolidBrush innerBrush = new SolidBrush(Color.FromArgb(220, 20, 20, 20))) // Dark grey
                {
                    g.FillEllipse(innerBrush, 30, 30, size - 60, size - 60);
                }

                // J Text
                using (Font font = new Font("Segoe UI", 100, FontStyle.Bold))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("J", font, textBrush, new RectangleF(0, 0, size, size), sf);
                }
            }

            // Save as PNG first
            string pngPath = @"c:\GenAI\Jarvis_desktop\src\JarvisDesktop\Assets\icon.png";
            bmp.Save(pngPath, ImageFormat.Png);
            
            // Note: We'll use the PNG for the Window Icon since WPF supports PNG icons
            Console.WriteLine("Icon created successfully at: " + pngPath);
        }
    }
}
