from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent.parent
assets = root / 'assets'
assets.mkdir(exist_ok=True)
points = [(147, 107), (353, 229), (262, 265), (220, 359)]
svg = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512"><rect x="8" y="8" width="496" height="496" rx="116" fill="#25664c"/><path d="M147 107L353 229L262 265L220 359Z" fill="#d8edc7" stroke="#d8edc7" stroke-width="18" stroke-linejoin="round"/><circle cx="220" cy="213" r="11" fill="#1d2b24"/><circle cx="264" cy="231" r="11" fill="#1d2b24"/></svg>'''
(assets / 'openclicky.svg').write_text(svg, encoding='utf-8')
image = Image.new('RGBA', (2048, 2048))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((32, 32, 2016, 2016), radius=464, fill='#25664c')
draw.polygon([(x*4,y*4) for x,y in points], fill='#d8edc7')
draw.line([(x*4,y*4) for x,y in points+[points[0]]], fill='#d8edc7', width=72, joint='curve')
for x,y in [(220,213),(264,231)]:
    draw.ellipse(((x-11)*4,(y-11)*4,(x+11)*4,(y+11)*4), fill='#1d2b24')
image = image.resize((512,512), Image.Resampling.LANCZOS)
image.save(assets / 'openclicky.png')
image.save(assets / 'openclicky.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
msix = root / 'packaging' / 'msix' / 'Assets'
msix.mkdir(exist_ok=True)
for name,size in [('StoreLogo',50),('Square44x44Logo',44),('Square150x150Logo',150)]:
    image.resize((size,size),Image.Resampling.LANCZOS).save(msix / (name+'.png'))
assert Image.open(assets / 'openclicky.png').size == (512,512)
assert Image.open(msix / 'Square44x44Logo.png').size == (44,44)
print('Created original SVG, Windows icon, and correctly sized package logos.')
