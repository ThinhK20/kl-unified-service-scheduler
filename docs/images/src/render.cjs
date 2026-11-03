// Renders each SVG given on the command line to a PNG next to docs/images (2x scale). Usage: node render.cjs a.svg b.svg
const { chromium } = require('playwright');
const fs = require('fs'); const path = require('path');
(async () => {
  const b = await chromium.launch();
  for (const f of process.argv.slice(2)) {
    const svg = fs.readFileSync(f, 'utf8');
    const w = +svg.match(/width="(\d+)"/)[1], h = +svg.match(/height="(\d+)"/)[1];
    const p = await b.newPage({ viewport: { width: w, height: h }, deviceScaleFactor: 2 });
    await p.setContent(`<html><body style="margin:0">${svg}</body></html>`);
    const out = path.join(path.dirname(f), '..', path.basename(f).replace('.svg', '.png'));
    await p.screenshot({ path: out, clip: { x: 0, y: 0, width: w, height: h } });
    console.log('wrote', out); await p.close();
  }
  await b.close();
})();
