const pages = process.argv.slice(2)
await Promise.all(pages.map(async page => {
  try {
    const response = await fetch(page, { signal: AbortSignal.timeout(25000) })
    const html = await response.text()
    const tags = [...html.matchAll(/<img\b[^>]*>|<meta\b[^>]*(?:og:image)[^>]*>/gi)].map(m => m[0])
    const match = tags.filter(tag => /camilo|filia|sofit|virtako|tilt|nominee|npk|urea|kali|dam-phu|starner|validacin|kasumin|fuji|zodiac|prevathon|og:image|đầu trâu|đạm phú|phú mỹ|phu_my/i.test(tag))
    console.log(JSON.stringify({ page, status: response.status, images: match.slice(0, 16), links: [...html.matchAll(/href=["']([^"']+)["']/g)].map(m => m[1]).filter(s => /camilo|dam-phu|kali-phu|16-16-8|starner|validacin/i.test(s)).slice(0, 8) }))
  } catch (err) { console.log(JSON.stringify({ page, error: err.message })) }
}))
