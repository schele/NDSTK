/*
    The scan log's colour schemes.

    Twenty named triples - background, ink, warning - chosen so every ink reads at 7:1 or better on
    its background and every warning at 4.5:1 or better, which is what a wall of 12px monospace
    needs to stay comfortable. The scheme sets three custom properties on the log panel and nothing
    else, so the panel's own styles stay the one place that decides how a log line looks.

    Data, not behaviour: app.js applies a scheme and remembers the choice; this file only says what
    the choices are. Fifteen dark, five light, the dark ones first because the default is one.
*/
export const LOG_THEMES = [
  { id: 'charcoal', name: 'Charcoal', bg: '#23272E', ink: '#D7DCE3', warn: '#FFC978' },
  { id: 'graphite', name: 'Graphite', bg: '#2B2F36', ink: '#DDE1E7', warn: '#FFD08A' },
  { id: 'midnight', name: 'Midnight', bg: '#0F1826', ink: '#C6D2E4', warn: '#FFC978' },
  { id: 'ink', name: 'Ink', bg: '#1B1F2A', ink: '#D2D7E1', warn: '#FFC978' },
  { id: 'slate', name: 'Slate', bg: '#2A3441', ink: '#D8E0EA', warn: '#FFC978' },
  { id: 'steel', name: 'Steel', bg: '#303A47', ink: '#DCE4EE', warn: '#FFD28C' },
  { id: 'storm', name: 'Storm', bg: '#3A4552', ink: '#E1E7EE', warn: '#FFD897' },
  { id: 'navy', name: 'Navy', bg: '#14213D', ink: '#CFD9EC', warn: '#FFC978' },
  { id: 'ocean', name: 'Ocean', bg: '#0F2B36', ink: '#CFE3EC', warn: '#FFD08A' },
  { id: 'pine', name: 'Pine', bg: '#16302B', ink: '#CFE5DE', warn: '#FFD37F' },
  { id: 'forest', name: 'Forest', bg: '#1B2A22', ink: '#D3E3D8', warn: '#FFD37F' },
  { id: 'plum', name: 'Plum', bg: '#2A2033', ink: '#E0D6EA', warn: '#FFCF8A' },
  { id: 'maroon', name: 'Maroon', bg: '#2F1B21', ink: '#EAD6DC', warn: '#FFD08A' },
  { id: 'espresso', name: 'Espresso', bg: '#2B221D', ink: '#E6DCD2', warn: '#FFD08A' },
  { id: 'ash', name: 'Ash', bg: '#42474F', ink: '#EEF0F3', warn: '#FFDCA0' },
  { id: 'paper', name: 'Paper', bg: '#F7F7F5', ink: '#1F2937', warn: '#8A5600' },
  { id: 'fog', name: 'Fog', bg: '#E9EEF5', ink: '#1B2434', warn: '#7F4F00' },
  { id: 'cream', name: 'Cream', bg: '#FBF4E4', ink: '#2B2416', warn: '#7F4F00' },
  { id: 'mint', name: 'Mint', bg: '#E7F4EF', ink: '#10302A', warn: '#764B00' },
  { id: 'sky', name: 'Sky', bg: '#E6F0FB', ink: '#122038', warn: '#7F4F00' },
];

/** What a fresh install shows: the dark grey the operator asked for in place of the old near-black. */
export const DEFAULT_LOG_THEME = 'charcoal';

/** The scheme for an id, or the default for an id this build does not know - a stale saved choice. */
export function logTheme(id) {
  return LOG_THEMES.find((theme) => theme.id === id) ?? LOG_THEMES.find((theme) => theme.id === DEFAULT_LOG_THEME);
}
