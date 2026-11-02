import { createRequire } from 'node:module'
import { mkdir, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

// Convert the companies' original artwork to PNG accepted by the existing upload API.
const require = createRequire(import.meta.url)
const sharp = require(process.env.SHARP_MODULE || 'sharp')
const assets = [
  ['ASIATIC', 'https://files-myxp-mobile.gevme.com/0f29bce6-9208-4122-b94c-f4edb6a68092/67a025cabc325da88098b697/1PWI9/image.png', 'https://www.gevme.com/site/asbf2025-sponsor-asiatic-agricultural-industries-pte-ltd'],
  ['ADC', 'https://www.adcvn.com/wp-content/uploads/2024/06/ADC-Logo-SVG.svg', 'https://www.adcvn.com/'],
  ['BINH-DIEN', 'https://binhdien.vn/themes/vip/images/top-logo.png', 'https://binhdien.vn/'],
  ['CORTEVA', 'https://images.engage.corteva.com/EloquaImages/clients/DuPontAgricultureNA/%7B43e1c32f-a47c-48f3-9f90-3003216cba82%7D_corteva_logo_thumbnail.png', 'https://engage.corteva.com/media'],
  ['FMC', 'https://www.fmc.com/themes/custom/fmc_corp/images/logo-fmc-tagline-en.svg', 'https://www.fmc.com/en'],
  ['HOKKO', 'https://www.hokkochem.co.jp/wp-content/themes/hokkochem/img/sprite-1-se8106d55ae.png', 'https://www.hokkochem.co.jp/', { left: 0, top: 0, width: 229, height: 30 }],
  ['KUMIAI', 'https://www.kumiai-chem.co.jp/common/img/parts/logo.png', 'https://www.kumiai-chem.co.jp/'],
  ['NIHON-NOHYAKU', 'https://www.nichino.co.jp/contents/000025377.png', 'https://www.nichino.co.jp/'],
  ['PHU-MY', 'https://dpm.vn/public_folder/files_upload/202607/13cad28c01021b2663ffdbc20bd46872.webp', 'https://dpm.vn/'],
  ['SUMITOMO', 'https://www.sumitomo-chem.co.jp/assets/svg/logo.svg', 'https://www.sumitomo-chem.co.jp/'],
  ['SYNGENTA', 'https://www.syngenta.com/themes/custom/themekit/logo.svg', 'https://www.syngenta.com/'],
]
const directory = fileURLToPath(new URL('./assets/', import.meta.url))
await mkdir(directory, { recursive: true })
const manifest = []
for (const [code, source, website, crop] of assets) {
  const response = await fetch(source, { signal: AbortSignal.timeout(30000) })
  if (!response.ok) throw new Error(`${code}: HTTP ${response.status}`)
  const original = Buffer.from(await response.arrayBuffer())
  let picture = sharp(original, { density: 192 })
  if (crop) picture = picture.extract(crop)
  const png = await picture.resize({ width: 600, height: 400, fit: 'inside' }).png().toBuffer()
  await writeFile(`${directory}/${code}.png`, png)
  manifest.push({ Code: code, Source: `assets/${code}.png`, Original: source, Website: website })
  console.log(`PREPARED ${code}: ${png.length} bytes`)
}
await writeFile(new URL('./logos-prepared.json', import.meta.url), JSON.stringify(manifest, null, 2) + '\n')
