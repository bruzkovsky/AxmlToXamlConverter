using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace AxmlToXamlConverter
{
    internal class Exporter
    {
        public Task Export(string input, string output, string @namespace, bool overwrite, bool dataContext)
        {
            if (!overwrite && File.Exists(output)) throw new InvalidOperationException("Output file already exists, you have to set /o (overwrite) or delete the file.");

            XDocument inDocument;

            // load input document from file
            using (var stream = File.OpenRead(input))
                inDocument = XDocument.Load(stream);

            // get root element
            var root = inDocument.Root;
            if (root == null) return Task.FromResult(true);

            // handle root element
            var newElement = CreateElement(root);
            if (newElement == null) return Task.FromResult(true);

            // handle children
            CreateChildren(root, newElement);

            foreach (var element in newElement.DescendantsAndSelf())
            {
                var parent = element.Parent;
                var parentsLine = new StringBuilder();
                while (parent != null)
                {
                    parentsLine.Insert(0, $"{parent.Name.LocalName} / ");
                    parent = parent.Parent;
                }
                Console.WriteLine($"{parentsLine}{element.Name.LocalName}");
            }

            // create output document
            var outDocument = new XDocument();

            // add page type
            var page = new XElement
            (
                FormsNamespace + "ContentPage",
                new XAttribute(XNamespace.Xmlns + "x", XamlNamespace),
                new XAttribute(XamlNamespace + "Class", $"{@namespace}.{Path.GetFileNameWithoutExtension(output)}")
            );

            if (dataContext)
            {
                page.Add(new XAttribute(XNamespace.Xmlns + "d", BlendNameSpace));
                page.Add(new XAttribute(XNamespace.Xmlns + "mc", MarkupNamespace));
                page.Add(new XAttribute(XNamespace.Xmlns + "vm", "clr-namespace:vmNamespace;assembly={assembly}"));
                page.Add(new XAttribute(BlendNameSpace + "DataContext", "{d:DesignInstance vm:vmName}"));
            }

            page.Add(newElement);
            outDocument.AddFirst(page);

            // write document to output file
            if (File.Exists(output)) File.Delete(output);
            using (var stream = File.OpenWrite(output))
                outDocument.Save(stream);

            return Task.FromResult(true);
        }

        public XNamespace FormsNamespace { get; } = XNamespace.Get("http://xamarin.com/schemas/2014/forms");

        public XNamespace XamlNamespace { get; } = XNamespace.Get("http://schemas.microsoft.com/winfx/2009/xaml");

        public XNamespace BlendNameSpace { get; } = XNamespace.Get("http://schemas.microsoft.com/expression/blend/2008");

        public XNamespace MarkupNamespace { get; } = XNamespace.Get("http://schemas.openxmlformats.org/markup-compatibility/2006");

        private void CreateChildren(XContainer parent, XElement newParent)
        {
            foreach (var child in parent.Elements())
            {
                // handle child element
                var newElement = CreateElement(child, newParent);
                CreateChildren(child, newElement ?? newParent);
            }
        }

        private XElement CreateElement(XElement child, XElement parent = null)
        {
            var key = ElementMap.Keys.FirstOrDefault(k => child.Name.LocalName.Contains(k));
            if (key == null) return null;
            var element = new XElement(FormsNamespace + ElementMap[key]);
            parent?.Add(element);
            foreach (var attribute in child.Attributes())
            {
                Func<XElement, string, XAttribute> attributeResolver;
                if (AttributeMap.TryGetValue(attribute.Name.LocalName, out attributeResolver))
                    element.Add(attributeResolver(child, attribute.Value));
            }
            ApplyPadding(element, child);
            ApplyMargin(element, child);
            ApplyBindings(element, child);
            return element;
        }

        private void ApplyBindings(XElement to, XElement from)
        {
            var bindingsAttribute = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "MvxBind");

            if (bindingsAttribute == null) return;

            var bindings = bindingsAttribute.Value.Split(';').Select(s => s.Trim());

            foreach (var binding in bindings)
            {
                var parameters = binding.Split(',').Select(s => s.Trim()).ToArray();
                var path = parameters.FirstOrDefault();
                if (path == null) continue;
                var converter = parameters.ElementAtOrDefault(1);
                var converterParameter = parameters.ElementAtOrDefault(2);

                // get path and property
                XAttribute attribute = null;
                var keyValue = path.Split(' ').Select(s => s.Trim()).ToArray();
                if (keyValue.Length != 2) continue;

                var sb = new StringBuilder("{");
                sb.Append($"Binding {keyValue[1]}");
                if (converter != null)
                {
                    sb.Append($", {converter}");
                    if (converterParameter != null)
                    {
                        sb.Append($", {converterParameter}");
                    }
                }
                sb.Append("}");
                var bindingString = sb.ToString();

                switch (keyValue[0])
                {
                    case "Text":
                        attribute = to.Attributes().FirstOrDefault(a => a.Name.LocalName == "Text");
                        if (attribute == null)
                            attribute = new XAttribute("Text", bindingString);
                        else
                        {
                            attribute.Value = bindingString;
                            continue;
                        }
                        break;
                    case "Visibility":
                        attribute = new XAttribute("IsVisible", bindingString);
                        break;
                    case "ItemsSource":
                        attribute = new XAttribute("ItemsSource", bindingString);
                        break;
                    case "SelectedItem":
                        attribute = new XAttribute("SelectedItem", bindingString);
                        break;
                    case "Click":
                        attribute = new XAttribute("Command", bindingString);
                        break;
                    case "Checked":
                        attribute = new XAttribute("Checked", bindingString);
                        break;
                    case "Enabled":
                        attribute = new XAttribute("IsEnabled", bindingString);
                        break;
                }

                if (attribute != null)
                {
                    to.Add(attribute);
                }
            }
        }

        private void ApplyPadding(XElement to, XElement from)
        {
            var localName = to.Name.LocalName;
            if (localName == "Label" || localName == "ListView" || localName == "Checkbox" || localName == "Button" || localName == "Image") return;

            var padding = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "padding");
            if (padding != null)
            {
                to.Add(new XAttribute("Padding", padding.Value.Replace("dp", "")));
                return;
            }

            var paddingLeft = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "paddingLeft" || a.Name.LocalName == "paddingStart");
            var paddingTop = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "paddingTop");
            var paddingRight = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "paddingRight" || a.Name.LocalName == "paddingEnd");
            var paddingBottom = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "paddingBottom");

            if (paddingLeft == null && paddingTop == null && paddingRight == null && paddingBottom == null)
                return;

            to.Add(new XAttribute("Padding", $"{paddingLeft?.Value ?? "0"},{paddingTop?.Value ?? "0"},{paddingRight?.Value ?? "0"},{paddingBottom?.Value ?? "0"}".Replace("dp", "")));
        }

        private void ApplyMargin(XElement to, XElement from)
        {
            var margin = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "layout_margin");
            if (margin != null)
            {
                to.Add(new XAttribute("Margin", margin.Value.Replace("dp", "")));
                return;
            }

            var marginLeft = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "layout_marginLeft" || a.Name.LocalName == "layout_marginStart");
            var marginTop = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "layout_marginTop");
            var marginRight = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "layout_marginRight" || a.Name.LocalName == "layout_marginEnd");
            var marginBottom = from.Attributes().FirstOrDefault(a => a.Name.LocalName == "layout_marginBottom");

            if (marginLeft == null && marginTop == null && marginRight == null && marginBottom == null)
                return;

            to.Add(new XAttribute("Margin", $"{marginLeft?.Value ?? "0"},{marginTop?.Value ?? "0"},{marginRight?.Value ?? "0"},{marginBottom?.Value ?? "0"}".Replace("dp", "")));
        }


        /// <summary>
        /// Maps elements of the axml scheme to the xaml scheme.
        /// </summary>
        public Dictionary<string, string> ElementMap { get; set; } = new Dictionary<string, string>
        {
            { "ListView", "ListView" },
            { "TextView", "Label" },
            { "EditText", "Entry" },
            { "LinearLayout", "StackLayout" },
            { "RelativeLayout", "RelativeLayout" },
            { "FrameLayout", "ContentPresenter" },
            { "ImageView", "Image" },
            { "ScrollView", "ScrollView" },
            { "Button", "Button" },
            { "CheckBox", "Checkbox" },
        };

        /// <summary>
        /// Maps attributes from the axml scheme to the xaml scheme. The key is the name of the axml element, the value is a
        /// function that takes the original axml element and the value and returns the <see cref="XAttribute"/> for the
        /// xaml scheme element.
        /// </summary>
        public Dictionary<string, Func<XElement, string, XAttribute>> AttributeMap { get; set; } = new Dictionary<string, Func<XElement, string, XAttribute>>
        {
            { "layout_width", (e, s) =>
                {
                    switch (s)
                    {
                        case "fill_parent":
                        case "match_parent":
                            return new XAttribute("HorizontalOptions", "FillAndExpand");
                        case "wrap_content":
                            return new XAttribute("HorizontalOptions", "Fill");
                        case "0":
                        case "0dp":
                            return e.Attributes().Any(a => a.Name.LocalName == "layout_weight")
                                ? new XAttribute("HorizontalOptions", "FillAndExpand")
                                : new XAttribute("WidthRequest", 0);
                        default:
                            return s.EndsWith("dp") ? new XAttribute("WidthRequest", int.Parse(s.Remove(s.IndexOf("dp", StringComparison.Ordinal)))) : null;
                    }
                }
            },
            { "layout_height", (e, s) =>
                {
                    switch (s)
                    {
                        case "fill_parent":
                        case "match_parent":
                            return new XAttribute("VerticalOptions", "FillAndExpand");
                        case "wrap_content":
                            return new XAttribute("VerticalOptions", "Fill");
                        case "0":
                        case "0dp":
                            return e.Attributes().Any(a => a.Name.LocalName == "layout_weight")
                                ? new XAttribute("VerticalOptions", "FillAndExpand")
                                : new XAttribute("HeightRequest", 0);
                        default:
                            return s.EndsWith("dp") ? new XAttribute("HeightRequest", int.Parse(s.Remove(s.IndexOf("dp", StringComparison.Ordinal)))) : null;
                    }
                }
            },
            { "orientation", (e, s) => e.Name.LocalName == "LinearLayout" ? new XAttribute("Orientation", CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s)) : null },
            { "text", (e, s) => new XAttribute("Text", s) },
            { "src", (e, s) => new XAttribute("Source", $"{s.Split('/').Last()}.png") },
        };
    }
}