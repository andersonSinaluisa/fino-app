import { Image, type ImageSourcePropType } from 'react-native';

/**
 * Isotipo oficial de Fino (la "F" en dos tonos), recoloreado a la paleta
 * actual de la app (colors.primary / colors.accentSecondary) y con fondo
 * transparente. Si algún día llega un asset definitivo distinto, basta con
 * pasar `source` para reemplazarlo aquí sin tocar BrandHeader/LoginScreen.
 */
const DEFAULT_MARK: ImageSourcePropType = require('../../assets/brand/fino-mark.png');

interface BrandMarkProps {
  size?: number;
  source?: ImageSourcePropType;
}

export function BrandMark({ size = 40, source = DEFAULT_MARK }: BrandMarkProps) {
  // Proporción real del recorte (274x302) para que "size" fije el ancho sin
  // deformar la marca -- resizeMode="contain" ya evita el estiramiento, pero
  // reservar el alto correcto evita un salto de layout mientras carga.
  const height = size * (302 / 274);

  return <Image source={source} style={{ width: size, height }} resizeMode="contain" />;
}
