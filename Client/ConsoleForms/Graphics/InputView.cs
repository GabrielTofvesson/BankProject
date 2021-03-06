﻿using Client.ConsoleForms.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tofvesson.Collections;
using Tofvesson.Crypto;

namespace Client.ConsoleForms.Graphics
{
    public class InputView : TextView
    {
        public delegate void SubmissionListener(InputView view);
        public delegate bool TextEnteredListener(InputView view, InputField change, ConsoleKeyInfo info, bool triggered);

        public SubmissionListener SubmissionsListener { protected get; set; }
        public TextEnteredListener InputListener { protected get; set; }
        public ConsoleColor DefaultBackgroundColor { get; set; }
        public ConsoleColor DefaultTextColor { get; set; }
        public ConsoleColor DefaultSelectBackgroundColor { get; set; }
        public ConsoleColor DefaultSelectTextColor { get; set; }
        public InputField[] Inputs { get; private set; }
        private int selectedField;
        public int SelectedField
        {
            get => selectedField;
            set
            {
                selectedField = value;
                Dirty = true;
            }
        }
        private string[][] splitInputs;
        protected ViewData data;


        public InputView(ViewData parameters, LangManager lang) : base(parameters, lang)
        {
            int
                sBC = parameters.AttribueAsInt("textfield_select_color", (int)ConsoleColor.Gray),
                sTC = parameters.AttribueAsInt("text_select_color", (int)ConsoleColor.Black),
                BC = parameters.AttribueAsInt("field_noselect_color", (int)ConsoleColor.DarkGray),
                TC = parameters.AttribueAsInt("text_noselect_color", (int)ConsoleColor.Black);

            DefaultBackgroundColor = (ConsoleColor)BC;
            DefaultTextColor = (ConsoleColor)TC;
            DefaultSelectBackgroundColor = (ConsoleColor)sBC;
            DefaultSelectTextColor = (ConsoleColor)sTC;

            this.data = parameters;

            List<InputField> fields = new List<InputField>();
            foreach (var data in parameters.nestedData.GetFirst(d => d.Name.Equals("Fields")).nestedData)
                if (!data.Name.Equals("Field")) continue;
                else fields.Add(new InputField(data.InnerText, data.AttribueAsInt("max_length", -1))
                {
                    ShowText = !data.AttribueAsBool("hide", false),
                    Text = lang.MapIfExists(data.GetAttribute("default")),
                    InputTypeString = data.GetAttribute("input_type"),
                    TextColor = (ConsoleColor)data.AttribueAsInt("color_text", TC),
                    BackgroundColor = (ConsoleColor)data.AttribueAsInt("color_background", BC),
                    SelectTextColor = (ConsoleColor)data.AttribueAsInt("color_text_select", sTC),
                    SelectBackgroundColor = (ConsoleColor)data.AttribueAsInt("color_background_select", sBC)
                });

            Inputs = fields.ToArray();

            int max = ContentWidth;
            int computedSize = 0;
            splitInputs = new string[Inputs.Length][];
            for (int i = 0; i < Inputs.Length; ++i)
            {
                splitInputs[i] = ComputeTextDimensions(Inputs[i].Label.Split(' '));
                foreach (var input in splitInputs[i])
                    if (input.Length > max)
                        max = input.Length;
                computedSize += splitInputs[i].Length;
            }
            ContentHeight += computedSize + Inputs.Length * 2;
            if (ContentWidth < max) ContentWidth = max;
        }

        public int IndexOf(InputField field)
        {
            for (int i = 0; i < Inputs.Length; ++i)
                if (field.Equals(Inputs[i]))
                    return i;
            return -1;
        }

        protected override void _Draw(int left, ref int top)
        {
            DrawContent(left, ref top);
            DrawInputFields(left, ref top, 1);
        }

        protected void DrawInputFields(int left, ref int top, int spaceHeight)
        {

            for (int j = 0; j < Inputs.Length; ++j)
            {
                DrawEmptyPadding(left, ref top, spaceHeight);

                for (int i = 0; i < splitInputs[j].Length; ++i)
                {
                    Console.SetCursorPosition(left, top++);
                    Console.BackgroundColor = BackgroundColor;
                    Console.Write(splitInputs[j][i] + Filler(' ', ContentWidth - splitInputs[j][i].Length));
                }
                Console.SetCursorPosition(left, top++);
                

                // Draw field
                Console.BackgroundColor = j == selectedField ? Inputs[j].SelectBackgroundColor : Inputs[j].BackgroundColor;
                Console.ForegroundColor = j == selectedField ? Inputs[j].SelectTextColor : Inputs[j].TextColor;
                Console.Write(Inputs[j].ShowText ? Inputs[j].Text.Substring(Inputs[j].RenderStart, Inputs[j].SelectIndex - Inputs[j].RenderStart) : Filler('*', Inputs[j].SelectIndex - Inputs[j].RenderStart));
                if (j == selectedField) Console.BackgroundColor = ConsoleColor.DarkGray;
                Console.Write(Inputs[j].SelectIndex < Inputs[j].Text.Length ? Inputs[j].ShowText ? Inputs[j].Text[Inputs[j].SelectIndex] : '*' : ' ');
                if (j == selectedField) Console.BackgroundColor = Inputs[j].SelectBackgroundColor;
                int drawn = 0;
                if (Inputs[j].SelectIndex < Inputs[j].Text.Length)
                    Console.Write(
                        Inputs[j].ShowText ?
                        Inputs[j].Text.Substring(Inputs[j].SelectIndex + 1, drawn = Math.Min(ContentWidth + Inputs[j].SelectIndex - Inputs[j].RenderStart - 1, Inputs[j].Text.Length - Inputs[j].SelectIndex - 1)) :
                        Filler('*', drawn = Math.Min(ContentWidth + Inputs[j].SelectIndex - Inputs[j].RenderStart - 1, Inputs[j].Text.Length - Inputs[j].SelectIndex - 1))
                        );
                Console.Write(Filler(' ', ContentWidth - 1 - drawn - Inputs[j].SelectIndex + Inputs[j].RenderStart));
                Console.ForegroundColor = ConsoleColor.Black;
            }
        }

        public override bool HandleKeyEvent(ConsoleController.KeyEvent evt, bool inFocus, bool triggered)
        {
            bool changed = base.HandleKeyEvent(evt, inFocus, triggered);
            ConsoleKeyInfo info = evt.Event;
            if ((!triggered && (!evt.ValidEvent || !inFocus)) || Inputs.Length == 0) return changed;
            evt.ValidEvent = false;
            switch (info.Key)
            {
                case ConsoleKey.LeftArrow:
                    if (Inputs[selectedField].SelectIndex > 0)
                    {
                        if (Inputs[selectedField].RenderStart == Inputs[selectedField].SelectIndex--) --Inputs[selectedField].RenderStart;
                    }
                    else return changed;
                    break;
                case ConsoleKey.RightArrow:
                    if (Inputs[selectedField].SelectIndex < Inputs[selectedField].Text.Length)
                    {
                        if (++Inputs[selectedField].SelectIndex - Inputs[selectedField].RenderStart == ContentWidth) ++Inputs[selectedField].RenderStart;
                    }
                    else return changed;
                    break;
                case ConsoleKey.Tab:
                case ConsoleKey.DownArrow:
                    if (selectedField < Inputs.Length - 1) ++selectedField;
                    else return changed;
                    break;
                case ConsoleKey.UpArrow:
                    if (selectedField > 0) --selectedField;
                    else return changed;
                    break;
                case ConsoleKey.Backspace:
                    if (Inputs[selectedField].SelectIndex > 0)
                    {
                        if (InputListener?.Invoke(this, Inputs[selectedField], info, triggered) == false) break;
                        string text = Inputs[selectedField].Text;
                        Inputs[selectedField].Text = text.Substring(0, Inputs[selectedField].SelectIndex - 1);
                        if (Inputs[selectedField].SelectIndex < text.Length) Inputs[selectedField].Text += text.Substring(Inputs[selectedField].SelectIndex);
                        if (Inputs[selectedField].RenderStart == Inputs[selectedField].SelectIndex--) --Inputs[selectedField].RenderStart;
                    }
                    else return changed;
                    break;
                case ConsoleKey.Delete:
                    if (Inputs[selectedField].SelectIndex < Inputs[selectedField].Text.Length)
                    {
                        if (InputListener?.Invoke(this, Inputs[selectedField], info, triggered) == false) break;
                        string text = Inputs[selectedField].Text;
                        Inputs[selectedField].Text = text.Substring(0, Inputs[selectedField].SelectIndex);
                        if (Inputs[selectedField].SelectIndex + 1 < text.Length) Inputs[selectedField].Text += text.Substring(Inputs[selectedField].SelectIndex + 1);
                    }
                    else return changed;
                    break;
                case ConsoleKey.Enter:
                    ParseAction(data)();
                    SubmissionsListener?.Invoke(this);
                    return changed;
                case ConsoleKey.Escape:
                    return changed;
                default:
                    if (info.KeyChar != 0 && info.KeyChar != '\b' && info.KeyChar != '\r' && (Inputs[selectedField].Text.Length < Inputs[selectedField].MaxLength || Inputs[selectedField].MaxLength < 0) && Inputs[selectedField].IsValidChar(info.KeyChar))
                    {
                        if (InputListener?.Invoke(this, Inputs[selectedField], info, triggered) == false) break;
                        Inputs[selectedField].Text = Inputs[selectedField].Text.Substring(0, Inputs[selectedField].SelectIndex) + info.KeyChar + Inputs[selectedField].Text.Substring(Inputs[selectedField].SelectIndex);
                        if (++Inputs[selectedField].SelectIndex - Inputs[selectedField].RenderStart == ContentWidth) ++Inputs[selectedField].RenderStart;
                    }
                    else return changed;
                    break;
            }
            return true;
        }


        public enum InputType
        {
            Any,
            AlphaNumeric,
            Integer,
            Decimal,
            Alphabet
        }
        public sealed class InputField
        {
            public const char hide_char = '*';

            public string Label { get; private set; }
            public int MaxLength { get; private set; }
            public bool ShowText { get; set; }
            private string text;
            public string Text
            {
                get => text;

                internal set
                {
                    text = value;
                }
            }
            public int SelectIndex { get; set; }
            public InputType Input { get; set; }
            public ConsoleColor TextColor { get; set; }
            public ConsoleColor BackgroundColor { get; set; }
            public ConsoleColor SelectTextColor { get; set; }
            public ConsoleColor SelectBackgroundColor { get; set; }
            public string InputTypeString
            {
                get
                {
                    switch (Input)
                    {
                        case InputType.Any:
                            return "Any";
                        case InputType.AlphaNumeric:
                            return "AlphaNumeric";
                        case InputType.Integer:
                            return "Integer";
                        case InputType.Decimal:
                            return "Decimal";
                        case InputType.Alphabet:
                            return "Alphabet";
                    }
                    throw new SystemException("Invalid system state detected");
                }

                set
                {
                    switch (value.ToLower())
                    {
                        case "alphanumeric":
                            Input = InputType.AlphaNumeric;
                            break;
                        case "integer":
                            Input = InputType.Integer;
                            break;
                        case "decimal":
                            Input = InputType.Decimal;
                            break;
                        case "alphabet":
                            Input = InputType.Alphabet;
                            break;
                        default:
                            Input = InputType.Any;
                            break;
                    }
                }
            }
            internal int RenderStart { get; set; }

            public InputField(string label, int maxLength)
            {
                TextColor = ConsoleColor.Black;
                BackgroundColor = ConsoleColor.DarkGray;
                SelectTextColor = ConsoleColor.Black;
                SelectBackgroundColor = ConsoleColor.Gray;
                Input = InputType.Any;
                Label = label;
                MaxLength = maxLength;
                Text = "";
            }

            public bool IsValidChar(char c) =>
                (Input == InputType.Any) ||
                (Input == InputType.AlphaNumeric && c.IsAlphaNumeric()) ||
                (Input == InputType.Alphabet && c.IsAlphabetical()) ||
                (Input == InputType.Integer && c.IsNumber()) ||
                (Input == InputType.Decimal && c.IsDecimal());

            public void ClearText()
            {
                Text = "";
                SelectIndex = 0;
                RenderStart = 0;
            }
        }
    }
}
