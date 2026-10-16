using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    /// <summary>
    /// NestoAPI#630: umbrales del motor de tendencias. Todos salen de appSettings <c>PropuestasStock:&lt;Nombre&gt;</c> y, si no
    /// están (las claves puestas a mano en el Web.config del servidor se pierden al publicar), del valor por defecto de aquí.
    /// <para><b>PROVISIONALES</b> hasta ver la primera calibración real (hoja «Calibración» del Excel): son valores razonables
    /// de partida, no salen todavía de los datos. La épica (#628) manda que los umbrales salgan del backtest y se documenten en
    /// la issue.</para>
    /// </summary>
    public class ParametrosTendencias
    {
        internal const string PREFIJO = "PropuestasStock:";
        public const string CLAVE_DESTINATARIOS = PREFIJO + "Destinatarios";
        public const string DESTINATARIOS_POR_DEFECTO = Constantes.Correos.INFORMATICA;

        /// <summary>Lo que le gustaría a Carlos como mucho (épica): 90 días. Nunca por debajo de la alcanzable del proveedor.</summary>
        public int CoberturaObjetivoDias { get; set; } = 90;
        /// <summary>Los 7 días de seguridad de la fórmula de hoy (ControlesStockController).</summary>
        public int DiasSeguridad { get; set; } = 7;
        /// <summary>Si el producto no tiene proveedor (o no se encuentra), los días en servir que se suponen.</summary>
        public int DiasEnServirPorDefecto { get; set; } = 15;

        /// <summary>Fase A (nuevo): menos de estos meses desde la primera venta. La épica dice «hasta 9-12 meses».</summary>
        public int MesesFaseA { get; set; } = 12;
        /// <summary>Fase C (intermitente): menos de estas unidades en 12 meses. La épica dice «menos de 10-12 uds/año».</summary>
        public int UnidadesAnioIntermitente { get; set; } = 12;

        /// <summary>Pendiente relativa (% al mes) a partir de la cual se dice que sube.</summary>
        public decimal PendienteSube { get; set; } = 2m;
        /// <summary>Pendiente relativa (% al mes) a partir de la cual (hacia abajo) se dice que baja.</summary>
        public decimal PendienteBaja { get; set; } = -3m;
        /// <summary>Para confirmar el declive, los últimos 12 meses tienen que estar a este % del pico de 5 años o menos.</summary>
        public decimal PorcentajePicoDeclive { get; set; } = 70m;
        /// <summary>Para confirmar el declive, el último trimestre por debajo de esta fracción del mismo trimestre del año anterior.</summary>
        public decimal RatioTrimestreDeclive { get; set; } = 0.9m;

        /// <summary>Proponer estado 1 (sobre pedido, sin máximos) si la cobertura pasa de estos meses y el declive está confirmado.</summary>
        public int MesesCoberturaEstado1 { get; set; } = 6;
        /// <summary>Proponer liquidar (Outlet / Ganavisiones) si está en declive y la cobertura pasa de estos meses.</summary>
        public int MesesCoberturaLiquidar { get; set; } = 12;
        /// <summary>Sin ninguna venta en estos meses (y fuera de la fase A) y con stock: liquidar.</summary>
        public int MesesSinSalidaLiquidar { get; set; } = 6;

        /// <summary>Fase A: el máximo no pasa de estos días de la velocidad demostrada.</summary>
        public int TopeDiasFaseA { get; set; } = 45;
        /// <summary>Fase A con recompra baja: tope más corto.</summary>
        public int TopeDiasFaseASinRecompra { get; set; } = 30;
        /// <summary>Tasa de recompra (%) por debajo de la cual un nuevo «no repite» (caso ImGolden).</summary>
        public decimal RecompraMinima { get; set; } = 20m;

        /// <summary>Estacionalidad: la familia necesita al menos estas unidades en los 36 meses (si no, ruido).</summary>
        public int UnidadesMinimasFamiliaEstacionalidad { get; set; } = 120;

        /// <summary>
        /// NestoAPI#655: días de seguridad de la fórmula P (fabricación propia, familias PRP). Más que los normales: se fabrican
        /// por lotes, con plazo largo, y no se pueden quedar cortos.
        /// </summary>
        public int DiasSeguridadPropios { get; set; } = 30;
        /// <summary>
        /// NestoAPI#655: vida útil de un producto propio cuyo grupo no está en <see cref="VidaUtilMesesPorGrupo"/> (lo conservador): el
        /// máximo nunca pasa de estos meses de venta.
        /// </summary>
        public int VidaUtilMesesPropios { get; set; } = 12;
        /// <summary>
        /// NestoAPI#655 (Carlos, 16/10/26): vida útil de los propios por grupo de producto, «GRUPO=meses» separados por punto y coma;
        /// 0 = no caduca (sin tope de vida útil, nunca «Caduca antes de venderse»). Cosmética 12 meses, peluquería 24; accesorios,
        /// aparatos y accesorios de peluquería no caducan. El grupo que no esté, <see cref="VidaUtilMesesPropios"/>.
        /// </summary>
        public string VidaUtilMesesPorGrupo { get; set; } = VIDA_UTIL_POR_GRUPO_POR_DEFECTO;
        public const string VIDA_UTIL_POR_GRUPO_POR_DEFECTO = "COS=12;PEL=24;ACC=0;APA=0;ACP=0";

        /// <summary>
        /// NestoAPI#656 (corte 2): una línea albaranada cuenta como «servida con retraso» si salió más de estos días naturales
        /// después de su fecha de entrega. 2 por defecto: el servicio normal sale el mismo día o el siguiente (ruta o agencia de
        /// 24 h), y 2 días absorben una fecha de entrega en sábado o domingo que se sirve el lunes; a partir de 3 días naturales ya
        /// es mercancía que no estaba cuando el cliente la quería.
        /// </summary>
        public int DiasRetraso { get; set; } = 2;

        /// <summary>Filas de la hoja «Top».</summary>
        public int FilasTop { get; set; } = 50;

        public static ParametrosTendencias DesdeConfiguracion() => Desde(clave => ConfigurationManager.AppSettings[clave]);

        public static ParametrosTendencias Desde(Func<string, string> leer)
        {
            ParametrosTendencias p = new ParametrosTendencias();
            if (leer == null)
            {
                return p;
            }
            p.CoberturaObjetivoDias = Entero(leer, nameof(CoberturaObjetivoDias), p.CoberturaObjetivoDias);
            p.DiasSeguridad = Entero(leer, nameof(DiasSeguridad), p.DiasSeguridad);
            p.DiasEnServirPorDefecto = Entero(leer, nameof(DiasEnServirPorDefecto), p.DiasEnServirPorDefecto);
            p.MesesFaseA = Entero(leer, nameof(MesesFaseA), p.MesesFaseA);
            p.UnidadesAnioIntermitente = Entero(leer, nameof(UnidadesAnioIntermitente), p.UnidadesAnioIntermitente);
            p.PendienteSube = Numero(leer, nameof(PendienteSube), p.PendienteSube);
            p.PendienteBaja = Numero(leer, nameof(PendienteBaja), p.PendienteBaja);
            p.PorcentajePicoDeclive = Numero(leer, nameof(PorcentajePicoDeclive), p.PorcentajePicoDeclive);
            p.RatioTrimestreDeclive = Numero(leer, nameof(RatioTrimestreDeclive), p.RatioTrimestreDeclive);
            p.MesesCoberturaEstado1 = Entero(leer, nameof(MesesCoberturaEstado1), p.MesesCoberturaEstado1);
            p.MesesCoberturaLiquidar = Entero(leer, nameof(MesesCoberturaLiquidar), p.MesesCoberturaLiquidar);
            p.MesesSinSalidaLiquidar = Entero(leer, nameof(MesesSinSalidaLiquidar), p.MesesSinSalidaLiquidar);
            p.TopeDiasFaseA = Entero(leer, nameof(TopeDiasFaseA), p.TopeDiasFaseA);
            p.TopeDiasFaseASinRecompra = Entero(leer, nameof(TopeDiasFaseASinRecompra), p.TopeDiasFaseASinRecompra);
            p.RecompraMinima = Numero(leer, nameof(RecompraMinima), p.RecompraMinima);
            p.UnidadesMinimasFamiliaEstacionalidad = Entero(leer, nameof(UnidadesMinimasFamiliaEstacionalidad), p.UnidadesMinimasFamiliaEstacionalidad);
            p.DiasSeguridadPropios = Entero(leer, nameof(DiasSeguridadPropios), p.DiasSeguridadPropios);
            p.VidaUtilMesesPropios = Entero(leer, nameof(VidaUtilMesesPropios), p.VidaUtilMesesPropios);
            string porGrupo = leer(PREFIJO + nameof(VidaUtilMesesPorGrupo));
            if (!string.IsNullOrWhiteSpace(porGrupo))
            {
                p.VidaUtilMesesPorGrupo = porGrupo.Trim();
            }
            p.DiasRetraso = Entero(leer, nameof(DiasRetraso), p.DiasRetraso);
            p.FilasTop = Entero(leer, nameof(FilasTop), p.FilasTop);
            return p;
        }

        /// <summary>Correos de <see cref="CLAVE_DESTINATARIOS"/> (separados por coma o punto y coma); por defecto, el de Carlos.</summary>
        public static List<string> Destinatarios(Func<string, string> leer)
        {
            string configurados = leer?.Invoke(CLAVE_DESTINATARIOS);
            return (string.IsNullOrWhiteSpace(configurados) ? DESTINATARIOS_POR_DEFECTO : configurados)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Contains("@"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Los umbrales en texto, para la hoja de calibración y el correo.</summary>
        public IEnumerable<(string Nombre, string Valor, string Significado)> Descripcion()
        {
            yield return (nameof(CoberturaObjetivoDias), CoberturaObjetivoDias.ToString(), "Cobertura objetivo en días (nunca por debajo de la alcanzable)");
            yield return (nameof(DiasSeguridad), DiasSeguridad.ToString(), "Días de seguridad");
            yield return (nameof(DiasEnServirPorDefecto), DiasEnServirPorDefecto.ToString(), "Días en servir si el producto no tiene proveedor");
            yield return (nameof(MesesFaseA), MesesFaseA.ToString(), "Fase A: menos de estos meses desde la primera venta");
            yield return (nameof(UnidadesAnioIntermitente), UnidadesAnioIntermitente.ToString(), "Fase C: menos de estas unidades en 12 meses");
            yield return (nameof(PendienteSube), PendienteSube.ToString(CultureInfo.InvariantCulture), "Sube: pendiente relativa (% al mes) desde aquí");
            yield return (nameof(PendienteBaja), PendienteBaja.ToString(CultureInfo.InvariantCulture), "Baja: pendiente relativa (% al mes) hasta aquí");
            yield return (nameof(PorcentajePicoDeclive), PorcentajePicoDeclive.ToString(CultureInfo.InvariantCulture), "Declive confirmado: 12 meses a este % del pico o menos");
            yield return (nameof(RatioTrimestreDeclive), RatioTrimestreDeclive.ToString(CultureInfo.InvariantCulture), "Declive confirmado: trimestre por debajo de esta fracción del año anterior");
            yield return (nameof(MesesCoberturaEstado1), MesesCoberturaEstado1.ToString(), "Estado 1 si declive y cobertura de más de estos meses");
            yield return (nameof(MesesCoberturaLiquidar), MesesCoberturaLiquidar.ToString(), "Liquidar si declive y cobertura de más de estos meses");
            yield return (nameof(MesesSinSalidaLiquidar), MesesSinSalidaLiquidar.ToString(), "Liquidar si no se vende nada en estos meses (salvo fase A)");
            yield return (nameof(TopeDiasFaseA), TopeDiasFaseA.ToString(), "Fase A: tope del máximo en días de velocidad");
            yield return (nameof(TopeDiasFaseASinRecompra), TopeDiasFaseASinRecompra.ToString(), "Fase A con recompra baja: tope en días");
            yield return (nameof(RecompraMinima), RecompraMinima.ToString(CultureInfo.InvariantCulture), "Fase A: recompra (%) por debajo de la cual se acorta el tope");
            yield return (nameof(UnidadesMinimasFamiliaEstacionalidad), UnidadesMinimasFamiliaEstacionalidad.ToString(), "Estacionalidad: mínimo de unidades de la familia en 36 meses");
            yield return (nameof(DiasSeguridadPropios), DiasSeguridadPropios.ToString(), "Propios (P): días de seguridad");
            yield return (nameof(VidaUtilMesesPorGrupo), VidaUtilMesesPorGrupo, "Propios (P): vida útil por grupo en meses de venta (tope del máximo); 0 = no caduca: " + TextoVidaUtil());
            yield return (nameof(DiasRetraso), DiasRetraso.ToString(), "Servido con retraso: albarán más de estos días después de la fecha de entrega (12 meses)");
            yield return (nameof(VidaUtilMesesPropios), VidaUtilMesesPropios.ToString(), "Propios (P): vida útil en meses de los grupos que no están en VidaUtilMesesPorGrupo");
        }

        /// <summary>«GRUPO=meses» de <see cref="VidaUtilMesesPorGrupo"/> (lo que no se entienda, se ignora).</summary>
        public Dictionary<string, int> VidaUtilPorGrupo()
        {
            Dictionary<string, int> resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string par in (VidaUtilMesesPorGrupo ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] partes = par.Split(new[] { '=', ':' }, 2);
                if (partes.Length == 2 && !string.IsNullOrWhiteSpace(partes[0])
                    && int.TryParse(partes[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int meses) && meses >= 0)
                {
                    resultado[partes[0].Trim()] = meses;
                }
            }
            return resultado;
        }

        /// <summary>
        /// NestoAPI#655: meses de vida útil de un propio de <paramref name="grupo"/>; null si no caduca. El grupo que no tiene regla,
        /// <see cref="VidaUtilMesesPropios"/> (lo conservador).
        /// </summary>
        public int? VidaUtilMeses(string grupo)
        {
            if (!string.IsNullOrWhiteSpace(grupo) && VidaUtilPorGrupo().TryGetValue(grupo.Trim(), out int meses))
            {
                return meses <= 0 ? (int?)null : meses;
            }
            return Math.Max(1, VidaUtilMesesPropios);
        }

        /// <summary>NestoAPI#655: si <paramref name="grupo"/> tiene regla propia en <see cref="VidaUtilMesesPorGrupo"/>.</summary>
        public bool TieneReglaVidaUtil(string grupo) => !string.IsNullOrWhiteSpace(grupo) && VidaUtilPorGrupo().ContainsKey(grupo.Trim());

        /// <summary>La vida útil en texto: «COS 12 meses, PEL 24 meses, ACC/APA/ACP no caducan; el resto, 12 meses».</summary>
        public string TextoVidaUtil()
        {
            Dictionary<string, int> porGrupo = VidaUtilPorGrupo();
            List<string> partes = porGrupo.Where(g => g.Value > 0).Select(g => $"{g.Key} {g.Value} meses").ToList();
            List<string> sinCaducidad = porGrupo.Where(g => g.Value <= 0).Select(g => g.Key).ToList();
            if (sinCaducidad.Count > 0)
            {
                partes.Add(string.Join("/", sinCaducidad) + (sinCaducidad.Count == 1 ? " no caduca" : " no caducan"));
            }
            string resto = $"el resto, {Math.Max(1, VidaUtilMesesPropios)} meses";
            return partes.Count == 0 ? resto : string.Join(", ", partes) + "; " + resto;
        }

        private static int Entero(Func<string, string> leer, string nombre, int porDefecto)
        {
            string valor = leer(PREFIJO + nombre);
            return !string.IsNullOrWhiteSpace(valor) && int.TryParse(valor.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : porDefecto;
        }

        private static decimal Numero(Func<string, string> leer, string nombre, decimal porDefecto)
        {
            string valor = leer(PREFIJO + nombre);
            return !string.IsNullOrWhiteSpace(valor)
                && decimal.TryParse(valor.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal n)
                ? n
                : porDefecto;
        }
    }
}
