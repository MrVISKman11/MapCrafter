using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ExileCore;

namespace MapCrafter.Services
{
    public static class CrafterInput
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly Random _rng = new();
        public static SharpDX.RectangleF WindowRect { get; set; }
        public static bool IsShiftDown { get; private set; }
        public static bool IsCtrlDown { get; private set; }

        public static bool IsEmergencyStopPressed()
        {
            return (GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0 ||
                   (GetAsyncKeyState((int)Keys.F8) & 0x8000) != 0;
        }

        public static Vector2 ClampToWindow(Vector2 pos)
        {
            if (WindowRect.Width < 50 || WindowRect.Height < 50)
                return pos;

            float margin = 20f;
            float x = Math.Clamp(pos.X, WindowRect.Left + margin, WindowRect.Right - margin);
            float y = Math.Clamp(pos.Y, WindowRect.Top + margin, WindowRect.Bottom - margin);
            return new Vector2(x, y);
        }

        public static async Task MoveCursorTo(Vector2 target, float jitter = 1.5f)
        {
            if (IsEmergencyStopPressed()) throw new OperationCanceledException("Emergency stop detected during cursor move.");

            target = ClampToWindow(target);
            var cur = Input.MousePosition;
            var start = new Vector2(cur.X, cur.Y);
            var delta = target - start;
            var dist = delta.Length();

            if (dist < 5f)
            {
                Input.SetCursorPos(target);
                return;
            }

            // Natural human movement duration: 80ms - 220ms proportional to distance
            int totalMs = Math.Clamp((int)(dist * 0.15f), 80, 220);
            int steps = Math.Clamp((int)(dist / 70f), 6, 12);
            int stepDelay = Math.Max(10, totalMs / steps);

            for (int i = 1; i <= steps; i++)
            {
                if (IsEmergencyStopPressed()) throw new OperationCanceledException("Emergency stop detected during cursor move.");

                float t = (float)i / steps;
                float smoothT = t * t * (3f - 2f * t);
                var interp = Vector2.Lerp(start, target, smoothT);

                if (i < steps && jitter > 0)
                {
                    float rx = ((float)_rng.NextDouble() - 0.5f) * jitter;
                    float ry = ((float)_rng.NextDouble() - 0.5f) * jitter;
                    interp += new Vector2(rx, ry);
                }

                Input.SetCursorPos(ClampToWindow(interp));
                await Task.Delay(stepDelay);
            }

            Input.SetCursorPos(target);
        }

        public static async Task RightClickAt(Vector2 pos, int delayMs = 120)
        {
            if (IsEmergencyStopPressed()) throw new OperationCanceledException("Emergency stop detected.");
            await MoveCursorTo(pos);

            // Pre-click settle: let PoE UI hover detection fire and slot highlight appear
            int preDelay = Math.Max(200, delayMs + _rng.Next(30));
            await Task.Delay(preDelay);
            if (IsEmergencyStopPressed()) throw new OperationCanceledException("Emergency stop detected.");

            Input.RightDown();
            await Task.Delay(50 + _rng.Next(20));
            Input.RightUp();

            // Post-click settle: let PoE game client register currency pickup
            int postDelay = Math.Max(180, delayMs + _rng.Next(30));
            await Task.Delay(postDelay);
        }

        public static async Task LeftClickAt(Vector2 pos, int delayMs = 120)
        {
            if (IsEmergencyStopPressed()) throw new OperationCanceledException("Emergency stop detected.");
            await MoveCursorTo(pos);

            int preDelay = Math.Max(120, (delayMs / 2) + _rng.Next(25));
            await Task.Delay(preDelay);
            if (IsEmergencyStopPressed()) throw new OperationCanceledException("Emergency stop detected.");

            Input.LeftDown();
            await Task.Delay(45 + _rng.Next(20));
            Input.LeftUp();

            int postDelay = Math.Max(120, (delayMs / 2) + _rng.Next(25));
            await Task.Delay(postDelay);
        }

        /// <summary>
        /// Left click without moving the cursor, using humanized hold and settle timings.
        /// </summary>
        public static async Task LeftClickInPlace(int delayMs = 120)
        {
            if (IsEmergencyStopPressed()) throw new OperationCanceledException("Emergency stop detected.");

            Input.LeftDown();
            await Task.Delay(45 + _rng.Next(20));
            Input.LeftUp();

            int postDelay = Math.Max(90, (delayMs / 3) + _rng.Next(25));
            await Task.Delay(postDelay);
        }

        public static void SetCtrl(bool down)
        {
            if (down && !IsCtrlDown)
            {
                Input.KeyDown(Keys.ControlKey);
                Input.KeyDown(Keys.LControlKey);
                IsCtrlDown = true;
            }
            else if (!down && IsCtrlDown)
            {
                Input.KeyUp(Keys.ControlKey);
                Input.KeyUp(Keys.LControlKey);
                IsCtrlDown = false;
            }
        }

        public static void SetShift(bool down)
        {
            if (down && !IsShiftDown)
            {
                Input.KeyDown(Keys.ShiftKey);
                Input.KeyDown(Keys.LShiftKey);
                IsShiftDown = true;
            }
            else if (!down && IsShiftDown)
            {
                Input.KeyUp(Keys.ShiftKey);
                Input.KeyUp(Keys.LShiftKey);
                IsShiftDown = false;
            }
        }

        public static void ReleaseAll()
        {
            SetCtrl(false);
            SetShift(false);
            Input.KeyUp(Keys.ControlKey);
            Input.KeyUp(Keys.LControlKey);
            Input.KeyUp(Keys.ShiftKey);
            Input.KeyUp(Keys.LShiftKey);
            Input.LeftUp();
            Input.RightUp();
        }
    }
}
