import { createRequire } from 'node:module'
import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

const sharp = createRequire(import.meta.url)(process.env.SHARP_MODULE || 'sharp')
// Real product packaging. Sizes may differ from the store's repacked/sale units.
// DEMO-HUUCO is a representative 20 kg organic fertilizer, approved by the operator.
const assets = [
  ['CAMILO-150SC-100', 'https://cdn.hstatic.net/products/200001200981/ce__-_250ml_demo__front___01-08-2025__1e092088eaeb4158a8469259b9851cd5_e3642e43eb0e402a9dbef092a9eff3cf_grande.png', 'https://www.adcshop.vn/products/camilo-150sc-lua-250ml'],
  ['DEMO-URE', 'https://dpm.vn/public_folder/files_upload/202502/urea-phu-my-mat-truocpng-1738742195.webp', 'https://dpm.vn/san-pham/dam-phu-my-28'],
  ['DEMO-FILIA525', 'https://www.syngenta.com.vn/sites/g/files/kgtney1261/files/styles/syngenta_large_4_3/public/media/image/2024/04/23/filia-02.png?itok=4r74OsQe', 'https://www.syngenta.com.vn/filiar-525se'],
  ['FUJIONE-40EC-100', 'https://bizweb.dktcdn.net/thumb/grande/100/615/481/products/25c4aadf-f3c2-437e-a607-f3f6225e584b.png?v=1764738106333', 'https://www.vipesco.com.vn/san-pham/thuoc-tru-benh/fujione-40ec.html'],
  ['DEMO-KALI', 'https://dpm.vn/public_folder/files_upload/202506/kali-phu-my-bot-mat-truocpng-1750056656.webp', 'https://dpm.vn/san-pham/kali-phu-my-79'],
  ['KASUMIN-2SL-100', 'https://product.hstatic.net/1000238788/product/kasumin-2sl-400ml-2024-anbio-vn_405a767b04264c8c96fa916c3ba97a12_large.jpg', 'https://www.anbio.vn/products/kasumin-2sl'],
  ['LUA-DT8-10', 'https://product.hstatic.net/200000563169/product/s_e58fc346baee4a01834f3027bb31a22d_grande.jpg', 'https://hatgiongtruongphuc.vn/products/dai-thom-8-vinaseed'],
  ['LUA-OM5451-10', 'https://loctroi.vn/UploadFiles/HinhDaiDien/010725031100806_OM5451-XN1.png', 'https://loctroi.vn/san-pham/33-vat-tu-nong-nghiep/36-giong-cay-trong/195-lua-thuan-chat-luong-cao-om5451'],
  ['NOMINEE-10SC-100', 'https://www.vfc.com.vn/storage/san-pham/nominee-10sc-100ml.png', 'https://www.vfc.com.vn/products/nominee-10sc'],
  ['DAUTRAU-NPK-202015', 'https://binhdien.vn/images/npk-dau-trau/202015.jpg', 'https://binhdien.vn/sanpham/npk-dau-trau/npk-dau-trau-20-20-15-te.html'],
  ['DEMO-NPK-168', 'https://dpm.vn/public_folder/files_upload/202502/npk-phu-my-16-16-813ste-mat-truocpng-1738824860.webp', 'https://dpm.vn/san-pham/npk-phu-my-16-16-813ste-70'],
  ['DEMO-HUUCO', 'https://ketnoinongnghiepxanh.com/wp-content/uploads/2021/12/900-900_11zon.webp', 'https://ketnoinongnghiepxanh.com/san-pham/phan-huu-co-han-quoc-vien-no-bao-20kg/'],
  ['PREVATHON-5SC-100', 'https://ag.fmc.com/vn/sites/default/files/2025-11/Prevathon-SC-1440x916-card_0.jpg', 'https://ag.fmc.com/vn/vi/san-pham/thuoc-tru-sau'],
  ['DEMO-SOFIT300', 'https://www.syngenta.com.vn/sites/g/files/kgtney1261/files/styles/syngenta_large_4_3/public/media/image/2024/04/12/2023.01.09_sofit-03.png?itok=9cTHyk7_', 'https://www.syngenta.com.vn/sofitr-300ec'],
  ['STARNER-20WP-20G', 'https://product.hstatic.net/1000238788/product/thuoc-tru-benh-starner-20wp-goi-100g-anbio-vn-2020-pts_b854fff51fc64b30a1ef266a09dda053_large.jpg', 'https://www.anbio.vn/products/starner-20wp-goi-100g'],
  ['TILTSUPER-300EC-100', 'https://www.syngenta.com.vn/sites/g/files/kgtney1261/files/styles/syngenta_large_4_3/public/media/image/2024/04/23/tilt-02.png?itok=JYANFYrK', 'https://www.syngenta.com.vn/tilt-superr-300ec'],
  ['VALIDACIN-5L-500', 'https://cdn.hstatic.net/products/1000238788/validacin-5sl-450ml-anbio-vn_fb9e6b3d2b804d1a9ed3fec144d803a2_grande.jpg', 'https://www.anbio.vn/products/thuoc-tru-benh-cay-validacin-5sl-vua-diet-nam-vua-diet-khuan-chai-450ml'],
  ['VIRTAKO-40WG-3G', 'https://vattucaycanh.com/public/userfiles/product/5_1.%5B16%5D.jpg', 'https://vattucaycanh.com/san-pham/thuoc-tru-sau-virtako-4-0-wg-1108.html'],
  ['ZODIAC-80WP-100G', 'https://hscagro.com/upload/product/08-9959.jpg', 'https://hscagro.com/vi/san-pham/zodiac-80wp-het-vang-la-sach-benh-tot-cay-59.html'],
]
const directory = fileURLToPath(new URL('./assets/products/', import.meta.url))
await mkdir(directory, { recursive: true })
const manifest = []
for (const [sku, original, website] of assets) {
  try {
    try {
      if (process.argv.includes('--refresh')) throw new Error('Refresh requested')
      await readFile(`${directory}/${sku}.png`)
      manifest.push({ Sku: sku, Source: `assets/products/${sku}.png`, Original: original, Website: website })
      console.log(`CACHED ${sku}`)
      continue
    } catch { /* Download images not yet prepared. */ }
    let bytes
    try {
      const response = await fetch(original, { signal: AbortSignal.timeout(25000) })
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      bytes = Buffer.from(await response.arrayBuffer())
    } catch (error) {
      // Optional local copies of the same source downloaded with the Windows TLS stack.
      const fallback = { 'CAMILO-150SC-100': 'camilo-original.png', 'VALIDACIN-5L-500': 'validacin-original.jpg' }[sku]
      if (!fallback) throw error
      bytes = await readFile(`${directory}/${fallback}`)
    }
    const photo = await sharp(bytes).rotate().resize({ width: 800, height: 800, fit: 'inside', withoutEnlargement: true }).flatten({ background: '#fff' }).png().toBuffer()
    await writeFile(`${directory}/${sku}.png`, photo)
    manifest.push({ Sku: sku, Source: `assets/products/${sku}.png`, Original: original, Website: website })
    console.log(`PREPARED ${sku}: ${photo.length} bytes`)
  } catch (error) { console.log(`FAILED ${sku}: ${error.message}`) }
}
// A local contact sheet allows visual inspection before any database write.
const tiles = await Promise.all(manifest.map(async (entry, i) => ({
  input: await sharp(`${directory}/${entry.Sku}.png`).resize(190, 180, { fit: 'contain', background: '#fff' }).extend({ bottom: 35, background: '#fff' }).composite([{ input: Buffer.from(`<svg width="190" height="35"><text x="3" y="20" font-size="11">${entry.Sku}</text></svg>`), top: 180, left: 0 }]).png().toBuffer(),
  left: (i % 5) * 190, top: Math.floor(i / 5) * 215,
})))
await sharp({ create: { width: 950, height: Math.ceil(tiles.length / 5) * 215, channels: 3, background: '#fff' } }).composite(tiles).png().toFile(new URL('./products-preview.png', import.meta.url).pathname.replace(/^\/(\w:)/, '$1'))
// WebP keeps photographic packaging small enough for reliable storage uploads.
for (const entry of manifest) {
  const webp = await sharp(`${directory}/${entry.Sku}.png`).webp({ quality: 85 }).toBuffer()
  await writeFile(`${directory}/${entry.Sku}.webp`, webp)
  entry.Source = `assets/products/${entry.Sku}.webp`
  console.log(`WEBP ${entry.Sku}: ${webp.length} bytes`)
}
await writeFile(new URL('./products-prepared.json', import.meta.url), JSON.stringify(manifest, null, 2) + '\n')
if (manifest.length !== assets.length) process.exitCode = 1
