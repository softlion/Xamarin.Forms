using System.Diagnostics;
using System.Linq;
using Xamarin.Forms.Internals;
using UIKit;

namespace Xamarin.Forms.Platform.iOS
{
	public static partial class FontExtensions
	{
		// The default font on iOS switches when the font size goes above 20 points.
		// Also see: https://developer.apple.com/design/human-interface-guidelines/ios/visual-design/typography
		static readonly string DefaultFontName = UIFont.SystemFontOfSize(19).Name;
		static readonly string DefaultFontNameLarge = UIFont.SystemFontOfSize(20).Name;

		public static UIFont ToUIFont(this Font self) => ToNativeFont(self);

		internal static UIFont ToUIFont(this IFontElement element) => ToNativeFont(element);

		internal static UIFont _ToNativeFont(string family, float size, FontAttributes attributes)
		{
			var bold = (attributes & FontAttributes.Bold) != 0;
			var italic = (attributes & FontAttributes.Italic) != 0;

			if (family != null && family != DefaultFontName && family != DefaultFontNameLarge)
			{
				try
				{
					UIFont result = null;
					if (UIFont.FamilyNames.Contains(family))
					{
						var descriptor = new UIFontDescriptor().CreateWithFamily(family);

						if (bold || italic)
						{
							var traits = (UIFontDescriptorSymbolicTraits)0;
							if (bold)
								traits = traits | UIFontDescriptorSymbolicTraits.Bold;
							if (italic)
								traits = traits | UIFontDescriptorSymbolicTraits.Italic;

							descriptor = descriptor.CreateWithTraits(traits);
							result = UIFont.FromDescriptor(descriptor, size);
							if (result != null)
								return result;
						}
					}

					result = UIFont.FromName(family, size);
					if (result != null)
						return result;
				}
				catch
				{
					Debug.WriteLine("Could not load font named: {0}", family);
				}
			}

			if (bold && italic)
			{
				var defaultFont = UIFont.SystemFontOfSize(size);

				var descriptor = defaultFont.FontDescriptor.CreateWithTraits(UIFontDescriptorSymbolicTraits.Bold | UIFontDescriptorSymbolicTraits.Italic);
				return UIFont.FromDescriptor(descriptor, 0);
			}

			if (italic)
				return UIFont.ItalicSystemFontOfSize(size);

			if (bold)
				return UIFont.BoldSystemFontOfSize(size);

			return UIFont.SystemFontOfSize(size);
		}
	}
}