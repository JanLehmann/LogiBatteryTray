using System.Drawing;

namespace LogiBatteryTray.Rendering;

/// <summary>
/// Hand-tuned pixel bitmap glyphs and device silhouettes for razor-sharp 16px and 20px tray icons.
/// </summary>
internal static class PixelFont
{
    // 13x5 keyboard silhouette for 20px icons
    internal static readonly string[] Keyboard13x5 =
    [
        ".###########.",
        "#.#.#.#.#.#.#",
        "#...........#",
        "#..#######..#",
        ".###########."
    ];

    // 7x6 mouse silhouette for 20px icons
    internal static readonly string[] Mouse7x6 =
    [
        "..###..",
        ".#.#.#.",
        "#..#..#",
        "#.....#",
        ".#...#.",
        "..###.."
    ];

    // 7x5 generic device/battery silhouette for 20px icons
    internal static readonly string[] Other7x5 =
    [
        "######.",
        "#....##",
        "#.##.##",
        "#....##",
        "######."
    ];

    // 11x5 keyboard silhouette for 16px icons
    internal static readonly string[] Keyboard11x5 =
    [
        ".#########.",
        "#.#.#.#.#.#",
        "#.........#",
        "#..#####..#",
        ".#########."
    ];

    // 5x5 mouse silhouette for 16px icons
    internal static readonly string[] Mouse5x5 =
    [
        ".###.",
        "#.#.#",
        "#...#",
        "#...#",
        ".###."
    ];

    // 5x5 mini keyboard silhouette for 20px Combined mode
    internal static readonly string[] MiniKeyboard5x5 =
    [
        "#####",
        "#.#.#",
        "#...#",
        ".###.",
        "#####"
    ];

    // 5x6 mini mouse silhouette for 20px Combined mode
    internal static readonly string[] MiniMouse5x6 =
    [
        ".###.",
        "#.#.#",
        "#.#.#",
        "#...#",
        "#...#",
        ".###."
    ];

    // 5x4 mini keyboard silhouette for 16px Combined mode
    internal static readonly string[] MiniKeyboard4x5 =
    [
        "#####",
        "#.#.#",
        "#...#",
        "#####"
    ];

    // 4x5 mini mouse silhouette for 16px Combined mode
    internal static readonly string[] MiniMouse4x5 =
    [
        ".##.",
        "#.##",
        "#..#",
        "#..#",
        ".##."
    ];

    // 4x6 charging bolt for 20px Split mode
    internal static readonly string[] Bolt4x6 =
    [
        "..##",
        ".##.",
        "####",
        ".##.",
        ".#..",
        "#..."
    ];

    // 3x5 charging bolt for 16px Split mode
    internal static readonly string[] Bolt3x5 =
    [
        ".##",
        "##.",
        "###",
        ".#.",
        "#.."
    ];

    // 9x6 headset silhouette for 20px Split mode
    internal static readonly string[] Headset9x6 =
    [
        "..#####..",
        ".#.....#.",
        "#.......#",
        "##.....##",
        "##.....##",
        ".#.....#."
    ];

    // 7x5 headset silhouette for 16px Split mode
    internal static readonly string[] Headset7x5 =
    [
        ".#####.",
        "#.....#",
        "##...##",
        "##...##",
        ".#...#."
    ];

    // 5x5 mini headset silhouette for 20px Combined mode
    internal static readonly string[] MiniHeadset5x5 =
    [
        ".###.",
        "#...#",
        "#...#",
        "##.##",
        ".#.#."
    ];

    // 5x4 mini headset silhouette for 16px Combined mode
    internal static readonly string[] MiniHeadset5x4 =
    [
        ".###.",
        "#...#",
        "##.##",
        ".#.#."
    ];

    // 6x8 bold digits (4x8 for '1') for 20px Split mode
    private static readonly Dictionary<char, string[]> DigitsBold8 = new()
    {
        ['0'] =
        [
            ".####.",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            ".####."
        ],
        ['1'] =
        [
            ".##.",
            "###.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            "####"
        ],
        ['2'] =
        [
            ".####.",
            "##..##",
            "....##",
            "...##.",
            "..##..",
            ".##...",
            "##....",
            "######"
        ],
        ['3'] =
        [
            ".####.",
            "##..##",
            "....##",
            "..###.",
            "....##",
            "....##",
            "##..##",
            ".####."
        ],
        ['4'] =
        [
            "...###",
            "..####",
            ".##.##",
            "##..##",
            "######",
            "....##",
            "....##",
            "....##"
        ],
        ['5'] =
        [
            "######",
            "##....",
            "#####.",
            "....##",
            "....##",
            "....##",
            "##..##",
            ".####."
        ],
        ['6'] =
        [
            ".####.",
            "##..##",
            "##....",
            "#####.",
            "##..##",
            "##..##",
            "##..##",
            ".####."
        ],
        ['7'] =
        [
            "######",
            "##..##",
            "....##",
            "...##.",
            "..##..",
            "..##..",
            "..##..",
            "..##.."
        ],
        ['8'] =
        [
            ".####.",
            "##..##",
            "##..##",
            ".####.",
            "##..##",
            "##..##",
            "##..##",
            ".####."
        ],
        ['9'] =
        [
            ".####.",
            "##..##",
            "##..##",
            ".#####",
            "....##",
            "....##",
            "##..##",
            ".####."
        ],
        ['-'] =
        [
            ".....",
            ".....",
            ".....",
            "#####",
            "#####",
            ".....",
            ".....",
            "....."
        ]
    };

    // 5x8 compact digits (3x8 for '1') for 3-digit "100" in 20px Split mode
    private static readonly Dictionary<char, string[]> DigitsCompact8 = new()
    {
        ['0'] =
        [
            ".###.",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            ".###."
        ],
        ['1'] =
        [
            ".#.",
            "##.",
            ".#.",
            ".#.",
            ".#.",
            ".#.",
            ".#.",
            "###"
        ]
    };

    // 5x7 digits (3x7 for '1') for 16px Split mode
    private static readonly Dictionary<char, string[]> Digits7 = new()
    {
        ['0'] =
        [
            ".###.",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            ".###."
        ],
        ['1'] =
        [
            ".#.",
            "##.",
            ".#.",
            ".#.",
            ".#.",
            ".#.",
            "###"
        ],
        ['2'] =
        [
            ".###.",
            "##.##",
            "...##",
            "..##.",
            ".##..",
            "##...",
            "#####"
        ],
        ['3'] =
        [
            ".###.",
            "##.##",
            "...##",
            "..##.",
            "...##",
            "##.##",
            ".###."
        ],
        ['4'] =
        [
            "..###",
            ".####",
            "##.##",
            "#####",
            "...##",
            "...##",
            "...##"
        ],
        ['5'] =
        [
            "#####",
            "##...",
            "####.",
            "...##",
            "...##",
            "##.##",
            ".###."
        ],
        ['6'] =
        [
            ".###.",
            "##...",
            "##...",
            "####.",
            "##.##",
            "##.##",
            ".###."
        ],
        ['7'] =
        [
            "#####",
            "...##",
            "..##.",
            "..##.",
            ".##..",
            ".##..",
            ".##.."
        ],
        ['8'] =
        [
            ".###.",
            "##.##",
            "##.##",
            ".###.",
            "##.##",
            "##.##",
            ".###."
        ],
        ['9'] =
        [
            ".###.",
            "##.##",
            "##.##",
            ".####",
            "...##",
            "##.##",
            ".###."
        ],
        ['-'] =
        [
            "....",
            "....",
            "....",
            "####",
            "....",
            "....",
            "...."
        ]
    };

    // 4x7 compact digits (2x7 for '1') for 3-digit "100" in 16px Split mode
    private static readonly Dictionary<char, string[]> DigitsCompact7 = new()
    {
        ['0'] =
        [
            ".##.",
            "#..#",
            "#..#",
            "#..#",
            "#..#",
            "#..#",
            ".##."
        ],
        ['1'] =
        [
            "#.",
            "##",
            ".#",
            ".#",
            ".#",
            ".#",
            "##"
        ]
    };

    // 4x6 digits (2x6 for '1') for 20px Combined mode
    private static readonly Dictionary<char, string[]> Digits6 = new()
    {
        ['0'] =
        [
            ".##.",
            "#..#",
            "#..#",
            "#..#",
            "#..#",
            ".##."
        ],
        ['1'] =
        [
            ".#",
            "##",
            ".#",
            ".#",
            ".#",
            "##"
        ],
        ['2'] =
        [
            "###.",
            "...#",
            ".##.",
            "#...",
            "#...",
            "####"
        ],
        ['3'] =
        [
            "###.",
            "...#",
            ".##.",
            "...#",
            "...#",
            "###."
        ],
        ['4'] =
        [
            "#..#",
            "#..#",
            "####",
            "...#",
            "...#",
            "...#"
        ],
        ['5'] =
        [
            "####",
            "#...",
            "###.",
            "...#",
            "...#",
            "###."
        ],
        ['6'] =
        [
            ".##.",
            "#...",
            "###.",
            "#..#",
            "#..#",
            ".##."
        ],
        ['7'] =
        [
            "####",
            "...#",
            "..#.",
            ".#..",
            ".#..",
            ".#.."
        ],
        ['8'] =
        [
            ".##.",
            "#..#",
            ".##.",
            "#..#",
            "#..#",
            ".##."
        ],
        ['9'] =
        [
            ".##.",
            "#..#",
            ".###",
            "...#",
            "...#",
            ".##."
        ],
        ['-'] =
        [
            "...",
            "...",
            "###",
            "###",
            "...",
            "..."
        ]
    };

    // 3x5 digits (2x5 for '1') for 16px Combined mode
    private static readonly Dictionary<char, string[]> Digits5 = new()
    {
        ['0'] =
        [
            "###",
            "#.#",
            "#.#",
            "#.#",
            "###"
        ],
        ['1'] =
        [
            ".#",
            "##",
            ".#",
            ".#",
            "##"
        ],
        ['2'] =
        [
            "###",
            "..#",
            "###",
            "#..",
            "###"
        ],
        ['3'] =
        [
            "###",
            "..#",
            "###",
            "..#",
            "###"
        ],
        ['4'] =
        [
            "#.#",
            "#.#",
            "###",
            "..#",
            "..#"
        ],
        ['5'] =
        [
            "###",
            "#..",
            "###",
            "..#",
            "###"
        ],
        ['6'] =
        [
            "###",
            "#..",
            "###",
            "#.#",
            "###"
        ],
        ['7'] =
        [
            "###",
            "..#",
            "..#",
            ".#.",
            ".#."
        ],
        ['8'] =
        [
            "###",
            "#.#",
            "###",
            "#.#",
            "###"
        ],
        ['9'] =
        [
            "###",
            "#.#",
            "###",
            "..#",
            "###"
        ],
        ['-'] =
        [
            "...",
            "...",
            "###",
            "...",
            "..."
        ]
    };

    // 8x14 bold digits (5x14 for '1', 7x14 for '-') for 20px NumbersOnly mode
    private static readonly Dictionary<char, string[]> DigitsLarge14 = new()
    {
        ['0'] =
        [
            "..####..",
            ".######.",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            ".######.",
            "..####.."
        ],
        ['1'] =
        [
            "..###",
            ".####",
            "#####",
            "##.##",
            "...##",
            "...##",
            "...##",
            "...##",
            "...##",
            "...##",
            "...##",
            "...##",
            "#####",
            "#####"
        ],
        ['2'] =
        [
            ".######.",
            "########",
            "##....##",
            "......##",
            "......##",
            ".....##.",
            "....##..",
            "...##...",
            "..##....",
            ".##.....",
            "##......",
            "##......",
            "########",
            "########"
        ],
        ['3'] =
        [
            ".######.",
            "########",
            "##....##",
            "......##",
            "......##",
            "...####.",
            "...####.",
            "......##",
            "......##",
            "......##",
            "......##",
            "##....##",
            "########",
            ".######."
        ],
        ['4'] =
        [
            "....###.",
            "...####.",
            "..#####.",
            ".###.##.",
            "##...##.",
            "##...##.",
            "##...##.",
            "##...##.",
            "########",
            "########",
            ".....##.",
            ".....##.",
            ".....##.",
            ".....##."
        ],
        ['5'] =
        [
            "########",
            "########",
            "##......",
            "##......",
            "##......",
            "#######.",
            "########",
            "......##",
            "......##",
            "......##",
            "......##",
            "##....##",
            "########",
            ".######."
        ],
        ['6'] =
        [
            "..#####.",
            ".######.",
            "##......",
            "##......",
            "##......",
            "#######.",
            "########",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "########",
            ".######."
        ],
        ['7'] =
        [
            "########",
            "########",
            "......##",
            "......##",
            ".....##.",
            "....##..",
            "....##..",
            "...##...",
            "...##...",
            "..##....",
            "..##....",
            "..##....",
            "..##....",
            "..##...."
        ],
        ['8'] =
        [
            ".######.",
            "########",
            "##....##",
            "##....##",
            "##....##",
            ".######.",
            ".######.",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "########",
            ".######."
        ],
        ['9'] =
        [
            ".######.",
            "########",
            "##....##",
            "##....##",
            "##....##",
            "##....##",
            "########",
            ".#######",
            "......##",
            "......##",
            "......##",
            "##....##",
            ".######.",
            ".#####.."
        ],
        ['-'] =
        [
            ".......",
            ".......",
            ".......",
            ".......",
            ".......",
            ".......",
            "#######",
            "#######",
            "#######",
            ".......",
            ".......",
            ".......",
            ".......",
            "......."
        ]
    };

    // 6x13 compact bold digits (4x13 for '1', 5x13 for '-') for 20px NumbersOnly 3-digit "100"
    private static readonly Dictionary<char, string[]> DigitsCompact13 = new()
    {
        ['0'] =
        [
            ".####.",
            "######",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".####."
        ],
        ['1'] =
        [
            ".##.",
            "###.",
            "###.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            "####",
            "####"
        ],
        ['2'] =
        [
            ".####.",
            "######",
            "##..##",
            "....##",
            "....##",
            "...##.",
            "..##..",
            ".##...",
            "##....",
            "##....",
            "##....",
            "######",
            "######"
        ],
        ['3'] =
        [
            ".####.",
            "######",
            "##..##",
            "....##",
            "....##",
            "..###.",
            "..###.",
            "....##",
            "....##",
            "....##",
            "##..##",
            "######",
            ".####."
        ],
        ['4'] =
        [
            "...##.",
            "..###.",
            ".####.",
            "##.##.",
            "##.##.",
            "##.##.",
            "######",
            "######",
            "...##.",
            "...##.",
            "...##.",
            "...##.",
            "...##."
        ],
        ['5'] =
        [
            "######",
            "######",
            "##....",
            "##....",
            "##....",
            "#####.",
            "######",
            "....##",
            "....##",
            "....##",
            "##..##",
            "######",
            ".####."
        ],
        ['6'] =
        [
            ".####.",
            "#####.",
            "##....",
            "##....",
            "##....",
            "#####.",
            "######",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".####."
        ],
        ['7'] =
        [
            "######",
            "######",
            "....##",
            "....##",
            "...##.",
            "...##.",
            "..##..",
            "..##..",
            "..##..",
            ".##...",
            ".##...",
            ".##...",
            ".##..."
        ],
        ['8'] =
        [
            ".####.",
            "######",
            "##..##",
            "##..##",
            "##..##",
            ".####.",
            ".####.",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".####."
        ],
        ['9'] =
        [
            ".####.",
            "######",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".#####",
            "....##",
            "....##",
            "....##",
            "##..##",
            "######",
            ".####."
        ],
        ['-'] =
        [
            ".....",
            ".....",
            ".....",
            ".....",
            ".....",
            "#####",
            "#####",
            "#####",
            ".....",
            ".....",
            ".....",
            ".....",
            "....."
        ]
    };

    // 6x12 bold digits (4x12 for '1', 6x12 for '-') for 16px NumbersOnly mode
    private static readonly Dictionary<char, string[]> DigitsLarge12 = new()
    {
        ['0'] =
        [
            ".####.",
            "######",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".####."
        ],
        ['1'] =
        [
            ".##.",
            "###.",
            "###.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            ".##.",
            "####",
            "####"
        ],
        ['2'] =
        [
            ".####.",
            "######",
            "##..##",
            "....##",
            "....##",
            "...##.",
            "..##..",
            ".##...",
            "##....",
            "##....",
            "######",
            "######"
        ],
        ['3'] =
        [
            ".####.",
            "######",
            "##..##",
            "....##",
            "..###.",
            "..###.",
            "....##",
            "....##",
            "....##",
            "##..##",
            "######",
            ".####."
        ],
        ['4'] =
        [
            "...##.",
            "..###.",
            ".####.",
            "##.##.",
            "##.##.",
            "######",
            "######",
            "...##.",
            "...##.",
            "...##.",
            "...##.",
            "...##."
        ],
        ['5'] =
        [
            "######",
            "######",
            "##....",
            "##....",
            "#####.",
            "######",
            "....##",
            "....##",
            "....##",
            "##..##",
            "######",
            ".####."
        ],
        ['6'] =
        [
            ".####.",
            "#####.",
            "##....",
            "##....",
            "#####.",
            "######",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".####."
        ],
        ['7'] =
        [
            "######",
            "######",
            "....##",
            "...##.",
            "...##.",
            "..##..",
            "..##..",
            "..##..",
            ".##...",
            ".##...",
            ".##...",
            ".##..."
        ],
        ['8'] =
        [
            ".####.",
            "######",
            "##..##",
            "##..##",
            ".####.",
            ".####.",
            "##..##",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".####."
        ],
        ['9'] =
        [
            ".####.",
            "######",
            "##..##",
            "##..##",
            "##..##",
            "######",
            ".#####",
            "....##",
            "....##",
            "##..##",
            "######",
            ".####."
        ],
        ['-'] =
        [
            "......",
            "......",
            "......",
            "......",
            "......",
            "######",
            "######",
            "......",
            "......",
            "......",
            "......",
            "......"
        ]
    };

    // 5x11 compact bold digits (3x11 for '1', 4x11 for '-') for 16px NumbersOnly 3-digit "100"
    private static readonly Dictionary<char, string[]> DigitsCompact11 = new()
    {
        ['0'] =
        [
            ".###.",
            "#####",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            "##.##",
            "#####",
            ".###."
        ],
        ['1'] =
        [
            ".#.",
            "##.",
            "##.",
            ".#.",
            ".#.",
            ".#.",
            ".#.",
            ".#.",
            ".#.",
            "###",
            "###"
        ],
        ['2'] =
        [
            ".###.",
            "#####",
            "##.##",
            "...##",
            "...##",
            "..##.",
            ".##..",
            "##...",
            "##...",
            "#####",
            "#####"
        ],
        ['3'] =
        [
            ".###.",
            "#####",
            "##.##",
            "...##",
            ".###.",
            ".###.",
            "...##",
            "...##",
            "##.##",
            "#####",
            ".###."
        ],
        ['4'] =
        [
            "...#.",
            "..##.",
            ".###.",
            "##.#.",
            "##.#.",
            "#####",
            "#####",
            "...#.",
            "...#.",
            "...#.",
            "...#."
        ],
        ['5'] =
        [
            "#####",
            "#####",
            "##...",
            "##...",
            "####.",
            "#####",
            "...##",
            "...##",
            "##.##",
            "#####",
            ".###."
        ],
        ['6'] =
        [
            ".###.",
            "####.",
            "##...",
            "##...",
            "####.",
            "#####",
            "##.##",
            "##.##",
            "##.##",
            "#####",
            ".###."
        ],
        ['7'] =
        [
            "#####",
            "#####",
            "...##",
            "...##",
            "..##.",
            "..##.",
            ".##..",
            ".##..",
            ".##..",
            ".##..",
            ".##.."
        ],
        ['8'] =
        [
            ".###.",
            "#####",
            "##.##",
            "##.##",
            ".###.",
            ".###.",
            "##.##",
            "##.##",
            "##.##",
            "#####",
            ".###."
        ],
        ['9'] =
        [
            ".###.",
            "#####",
            "##.##",
            "##.##",
            "#####",
            ".####",
            "...##",
            "...##",
            "##.##",
            "#####",
            ".###."
        ],
        ['-'] =
        [
            "....",
            "....",
            "....",
            "....",
            "####",
            "####",
            "....",
            "....",
            "....",
            "....",
            "...."
        ]
    };

    internal static void DrawGlyph(Bitmap bmp, string[] glyph, int startX, int startY, Color color)
    {
        for (int row = 0; row < glyph.Length; row++)
        {
            int py = startY + row;
            if (py < 0 || py >= bmp.Height)
            {
                continue;
            }

            string line = glyph[row];
            for (int col = 0; col < line.Length; col++)
            {
                if (line[col] != '#')
                {
                    continue;
                }

                int px = startX + col;
                if (px >= 0 && px < bmp.Width)
                {
                    bmp.SetPixel(px, py, color);
                }
            }
        }
    }

    internal static void DrawCenteredText8(Bitmap bmp, string text, int topY, Color color)
    {
        Dictionary<char, string[]> font = text.Length >= 3 ? DigitsCompact8 : DigitsBold8;
        int spacing = text.Length >= 3 ? 1 : 2;
        DrawCenteredString(bmp, text, font, spacing, topY, color);
    }

    internal static void DrawCenteredText7(Bitmap bmp, string text, int topY, Color color)
    {
        Dictionary<char, string[]> font = text.Length >= 3 ? DigitsCompact7 : Digits7;
        int spacing = 1;
        DrawCenteredString(bmp, text, font, spacing, topY, color);
    }

    internal static int MeasureText6(string text)
    {
        return MeasureString(text, Digits6, spacing: 1);
    }

    internal static void DrawText6(Bitmap bmp, string text, int startX, int topY, Color color)
    {
        DrawString(bmp, text, Digits6, spacing: 1, startX, topY, color);
    }

    internal static int MeasureText5(string text)
    {
        return MeasureString(text, Digits5, spacing: 1);
    }

    internal static void DrawText5(Bitmap bmp, string text, int startX, int topY, Color color)
    {
        DrawString(bmp, text, Digits5, spacing: 1, startX, topY, color);
    }

    internal static void DrawCenteredLarge20(Bitmap bmp, string text, Color color, int? topYOverride = null)
    {
        Dictionary<char, string[]> font = text.Length >= 3 ? DigitsCompact13 : DigitsLarge14;
        int spacing = text.Length >= 3 ? 1 : 2;
        int glyphHeight = text.Length >= 3 ? 13 : 14;
        int topY = topYOverride ?? (bmp.Height - glyphHeight) / 2;
        DrawCenteredString(bmp, text, font, spacing, topY, color);
    }

    internal static void DrawCenteredLarge16(Bitmap bmp, string text, Color color, int? topYOverride = null)
    {
        Dictionary<char, string[]> font = text.Length >= 3 ? DigitsCompact11 : DigitsLarge12;
        int spacing = text.Length >= 3 ? 1 : 2;
        int glyphHeight = text.Length >= 3 ? 11 : 12;
        int topY = topYOverride ?? (bmp.Height - glyphHeight) / 2;
        DrawCenteredString(bmp, text, font, spacing, topY, color);
    }

    private static void DrawCenteredString(
        Bitmap bmp,
        string text,
        IReadOnlyDictionary<char, string[]> font,
        int spacing,
        int topY,
        Color color)
    {
        int totalWidth = MeasureString(text, font, spacing);
        int startX = (bmp.Width - totalWidth) / 2;
        DrawString(bmp, text, font, spacing, startX, topY, color);
    }

    private static int MeasureString(string text, IReadOnlyDictionary<char, string[]> font, int spacing)
    {
        int total = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (font.TryGetValue(text[i], out string[]? glyph) && glyph.Length > 0)
            {
                if (total > 0)
                {
                    total += spacing;
                }

                total += glyph[0].Length;
            }
        }

        return total;
    }

    private static void DrawString(
        Bitmap bmp,
        string text,
        IReadOnlyDictionary<char, string[]> font,
        int spacing,
        int startX,
        int topY,
        Color color)
    {
        int x = startX;
        for (int i = 0; i < text.Length; i++)
        {
            if (!font.TryGetValue(text[i], out string[]? glyph) || glyph.Length == 0)
            {
                continue;
            }

            DrawGlyph(bmp, glyph, x, topY, color);
            x += glyph[0].Length + spacing;
        }
    }
}
