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
        /// <summary>NestoAPI#655: vida útil de un producto propio (simplificación de Carlos): el máximo nunca pasa de estos meses de venta.</summary>
        public int VidaUtilMesesPropios { get; set; } = 12;

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
            yield return (nameof(VidaUtilMesesPropios), VidaUtilMesesPropios.ToString(), "Propios (P): vida útil en meses de venta (tope del máximo)");
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
