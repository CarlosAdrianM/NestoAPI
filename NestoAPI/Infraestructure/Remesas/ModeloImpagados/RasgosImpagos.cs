using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Remesas.ModeloImpagados
{
    /// <summary>
    /// NestoAPI#896 (modelo en sombra): un recibo girado tal como lo lee <see cref="RepositorioModeloImpagos"/> de ExtractoCliente
    /// (cartera, TipoApunte 2, con Remesa). El día del GIRO es el de la remesa (<see cref="ReglaFechaGiro"/>): la Fecha del apunte de
    /// cartera es la de la factura, no la del giro.
    /// </summary>
    public class ReciboGirado
    {
        public string Empresa { get; set; }
        public int NumOrden { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Fecha del apunte de cartera (la de la factura).</summary>
        public DateTime FechaFactura { get; set; }
        public DateTime? FechaVto { get; set; }
        /// <summary>Día en que se giró (la remesa).</summary>
        public DateTime FechaGiro { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public double Importe { get; set; }
        public int? Remesa { get; set; }
        public string Vendedor { get; set; }
        public string FormaVenta { get; set; }
        /// <summary>Código de la ficha bancaria del cliente (ExtractoCliente.CCC), no la cuenta.</summary>
        public string Ccc { get; set; }
        /// <summary>Entidad (4 cifras) de esa ficha bancaria.</summary>
        public string Entidad { get; set; }
        /// <summary>Nombre del cliente (solo lo trae <see cref="IRepositorioModeloImpagos.LeerEfectosCartera"/>; no entra en los rasgos).</summary>
        [JsonIgnore]
        public string Nombre { get; set; }

        /// <summary>Día del impagado que se le ha enlazado (<see cref="EnlazadorImpagados"/>). Null = no ha vuelto.</summary>
        public DateTime? FechaImpago { get; set; }
        public string MotivoImpago { get; set; }

        /// <summary>LA ETIQUETA: devuelto en los <see cref="ConstructorRasgosImpagos.DIAS_ETIQUETA"/> días siguientes al giro.</summary>
        [JsonIgnore]
        public bool Devuelto => FechaImpago.HasValue && (FechaImpago.Value.Date - FechaGiro.Date).Days <= ConstructorRasgosImpagos.DIAS_ETIQUETA;

        internal string ClaveCliente => ConstructorRasgosImpagos.Clave(Empresa, Cliente);
        internal string ClaveEfecto => EnlazadorImpagados.Clave(Empresa, Cliente, Documento, Efecto);
    }

    /// <summary>Un apunte de impagado (TipoApunte 4; el de «Gastos Impagado…» no llega aquí): su Concepto es el motivo.</summary>
    public class ImpagadoLeido
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public DateTime Fecha { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Concepto { get; set; }
    }

    /// <summary>
    /// NestoAPI#896: el día del giro de un recibo. PURA. La remesa manda; sin ella (remesas antiguas sin fila), el vencimiento (dentro
    /// de una remesa los vencimientos distan 2 días de mediana); si tampoco, la fecha de la factura. Nunca antes de la factura.
    /// </summary>
    public static class ReglaFechaGiro
    {
        public static DateTime Calcular(DateTime fechaFactura, DateTime? fechaVto, DateTime? fechaRemesa)
        {
            DateTime factura = fechaFactura.Date;
            if (fechaRemesa.HasValue && fechaRemesa.Value.Date >= factura)
            {
                return fechaRemesa.Value.Date;
            }
            return fechaVto.HasValue && fechaVto.Value.Date >= factura ? fechaVto.Value.Date : factura;
        }
    }

    /// <summary>
    /// NestoAPI#896: enlaza cada impagado con su recibo. PURA. Por empresa + cliente + Nº Documento + Efecto, al ÚLTIMO giro anterior
    /// (o del mismo día) que todavía no tenga impagado; los impagados se recorren por fecha.
    /// </summary>
    public static class EnlazadorImpagados
    {
        internal static string Clave(string empresa, string cliente, string documento, string efecto)
            => $"{empresa?.Trim()}|{cliente?.Trim()}|{documento?.Trim()}|{efecto?.Trim()}".ToUpperInvariant();

        /// <summary>Rellena FechaImpago y MotivoImpago de los recibos (borra antes lo que tuvieran). Devuelve cuántos ha enlazado.</summary>
        public static int Enlazar(IEnumerable<ReciboGirado> recibos, IEnumerable<ImpagadoLeido> impagados)
        {
            var porClave = new Dictionary<string, List<ReciboGirado>>(StringComparer.Ordinal);
            foreach (ReciboGirado r in (recibos ?? Enumerable.Empty<ReciboGirado>()).Where(r => r != null))
            {
                r.FechaImpago = null;
                r.MotivoImpago = null;
                if (!porClave.TryGetValue(r.ClaveEfecto, out List<ReciboGirado> lista))
                {
                    porClave[r.ClaveEfecto] = lista = new List<ReciboGirado>();
                }
                lista.Add(r);
            }
            int enlazados = 0;
            foreach (ImpagadoLeido i in (impagados ?? Enumerable.Empty<ImpagadoLeido>()).Where(i => i != null).OrderBy(i => i.Fecha))
            {
                if (!porClave.TryGetValue(Clave(i.Empresa, i.Cliente, i.Documento, i.Efecto), out List<ReciboGirado> lista))
                {
                    continue;
                }
                ReciboGirado recibo = lista
                    .Where(r => !r.FechaImpago.HasValue && r.FechaGiro.Date <= i.Fecha.Date)
                    .OrderByDescending(r => r.FechaGiro).ThenByDescending(r => r.NumOrden)
                    .FirstOrDefault();
                if (recibo == null)
                {
                    continue;
                }
                recibo.FechaImpago = i.Fecha.Date;
                recibo.MotivoImpago = i.Concepto?.Trim();
                enlazados++;
            }
            return enlazados;
        }
    }

    /// <summary>Lo que el modelo recibe (ML.NET): todo conocido el día del giro. Los «no hay» van a -1.</summary>
    public class RasgosImpago
    {
        /// <summary>Motivo de su última devolución conocida (<see cref="MotivosImpago"/>).</summary>
        public string MotivoUltima { get; set; }
        public string FormaVenta { get; set; }
        /// <summary>Vendedor (los de pocos recibos van como <see cref="MapaCategoriasImpagos.OTROS"/>).</summary>
        public string Vendedor { get; set; }
        /// <summary>Entidad bancaria de la ficha del cliente (las de pocos recibos, <see cref="MapaCategoriasImpagos.OTROS"/>).</summary>
        public string Entidad { get; set; }

        /// <summary>Días desde su última devolución conocida; -1 si nunca ha devuelto.</summary>
        public float DiasDesdeUltimaDevolucion { get; set; }
        public float Devoluciones3Meses { get; set; }
        public float Devoluciones12Meses { get; set; }
        public float DevolucionesTotales { get; set; }
        /// <summary>Recibos del cliente girados antes de ese día.</summary>
        public float RecibosAnteriores { get; set; }
        public float Recibos12Meses { get; set; }
        /// <summary>Devoluciones conocidas / recibos anteriores; -1 si es su primer recibo.</summary>
        public float TasaDevolucionCliente { get; set; }
        /// <summary>Días desde su primer recibo; -1 si es el primero.</summary>
        public float AntiguedadDias { get; set; }
        /// <summary>Recibos suyos girados en los 45 días anteriores que ese día no constan devueltos (aún pueden volver).</summary>
        public float RecibosUltimos45 { get; set; }
        public float Importe { get; set; }
        /// <summary>Importe / mediana de sus recibos anteriores; -1 si no hay.</summary>
        public float RelacionImporteHabitual { get; set; }
        /// <summary>Este mismo documento y efecto ya se devolvió antes (se vuelve a girar).</summary>
        public float EsRegiro { get; set; }
        /// <summary>Nunca se le había girado por esa ficha bancaria.</summary>
        public float PrimerGiroCuenta { get; set; }
        /// <summary>Número del efecto de la factura (1, 2, 3…); 0 si no es un número.</summary>
        public float NumeroEfecto { get; set; }
        /// <summary>Días de la factura al giro.</summary>
        public float DiasFacturaAGiro { get; set; }
        public float DiaMes { get; set; }
        public float Mes { get; set; }
        /// <summary>0 = domingo … 6 = sábado.</summary>
        public float DiaSemana { get; set; }
        /// <summary>Devoluciones de todos los clientes en los 14 días anteriores / recibos girados de 24 a 8 días antes; -1 sin datos.</summary>
        public float NivelGeneral14 { get; set; }
        /// <summary>Lo mismo con 30 días (recibos de 40 a 8 días antes); -1 sin datos.</summary>
        public float NivelGeneral30 { get; set; }
    }

    /// <summary>Fila de entrenamiento: los rasgos y la etiqueta.</summary>
    public class FilaImpago : RasgosImpago
    {
        public bool Label { get; set; }
    }

    /// <summary>Salida del clasificador: la puntuación sin calibrar (la calibra <see cref="CalibradorPlatt"/>).</summary>
    public class PrediccionImpago
    {
        public float Score { get; set; }
    }

    /// <summary>Un ejemplo: el recibo, sus rasgos (categorías sin agrupar) y si volvió.</summary>
    public class EjemploImpago
    {
        public ReciboGirado Recibo { get; set; }
        public RasgosImpago Rasgos { get; set; }
        public bool Devuelto { get; set; }
        public DateTime FechaGiro => Recibo.FechaGiro.Date;
    }

    /// <summary>Motivos de devolución agrupados (por el Concepto del impagado). PURA.</summary>
    public static class MotivosImpago
    {
        public const string NINGUNA = "Ninguna";
        public const string SALDO = "Saldo";
        public const string MANDATO = "Mandato";
        public const string CUENTA = "Cuenta";
        public const string DISCONFORME = "Disconforme";
        public const string OTRO = "Otro";

        public static string Clasificar(string concepto)
        {
            string c = SinTildes(concepto);
            if (c.Length == 0)
            {
                return OTRO;
            }
            if (c.Contains("saldo insuficiente") || c.Contains("incorriente"))
            {
                return SALDO;
            }
            if (c.Contains("mandato"))
            {
                return MANDATO;
            }
            if (c.Contains("cuenta"))
            {
                return CUENTA;
            }
            return c.Contains("autorizada") || c.Contains("disconform") || c.Contains("deudor") ? DISCONFORME : OTRO;
        }

        private static string SinTildes(string texto)
        {
            string normal = (texto ?? string.Empty).Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            return new string(normal.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        }
    }

    /// <summary>
    /// NestoAPI#896: el nivel general de devoluciones (las «oleadas»): impagados de TODOS los clientes por día y recibos girados por día.
    /// PURA. Para un día solo mira días ANTERIORES.
    /// </summary>
    public class NivelGeneralImpagos
    {
        public const int MINIMO_RECIBOS_14 = 100;
        public const int MINIMO_RECIBOS_30 = 200;
        /// <summary>Los recibos de los últimos días no cuentan en el denominador: sus devoluciones aún no han llegado.</summary>
        public const int DIAS_SIN_MADURAR = 8;

        private readonly Dictionary<DateTime, int> recibosPorDia;
        private readonly Dictionary<DateTime, int> impagadosPorDia;
        private readonly Dictionary<DateTime, (float, float)> cache = new Dictionary<DateTime, (float, float)>();

        public NivelGeneralImpagos(IDictionary<DateTime, int> recibosPorDia, IDictionary<DateTime, int> impagadosPorDia)
        {
            this.recibosPorDia = Agrupar(recibosPorDia);
            this.impagadosPorDia = Agrupar(impagadosPorDia);
        }

        public static NivelGeneralImpagos Desde(IEnumerable<ReciboGirado> recibos, IEnumerable<ImpagadoLeido> impagados)
            => new NivelGeneralImpagos(
                (recibos ?? Enumerable.Empty<ReciboGirado>()).Where(r => r != null).GroupBy(r => r.FechaGiro.Date).ToDictionary(g => g.Key, g => g.Count()),
                (impagados ?? Enumerable.Empty<ImpagadoLeido>()).Where(i => i != null).GroupBy(i => i.Fecha.Date).ToDictionary(g => g.Key, g => g.Count()));

        public float Nivel14(DateTime dia) => Niveles(dia).Item1;

        public float Nivel30(DateTime dia) => Niveles(dia).Item2;

        private (float, float) Niveles(DateTime dia)
        {
            dia = dia.Date;
            lock (cache)
            {
                if (!cache.TryGetValue(dia, out (float, float) niveles))
                {
                    niveles = (Nivel(dia, 14, MINIMO_RECIBOS_14), Nivel(dia, 30, MINIMO_RECIBOS_30));
                    cache[dia] = niveles;
                }
                return niveles;
            }
        }

        private float Nivel(DateTime dia, int dias, int minimoRecibos)
        {
            int devueltos = Suma(impagadosPorDia, dia.AddDays(-dias), dia.AddDays(-1));
            int recibos = Suma(recibosPorDia, dia.AddDays(-dias - 10), dia.AddDays(-DIAS_SIN_MADURAR));
            return recibos >= minimoRecibos ? (float)devueltos / recibos : -1f;
        }

        private static int Suma(Dictionary<DateTime, int> porDia, DateTime desde, DateTime hasta)
        {
            int suma = 0;
            for (DateTime d = desde; d <= hasta; d = d.AddDays(1))
            {
                if (porDia.TryGetValue(d, out int n))
                {
                    suma += n;
                }
            }
            return suma;
        }

        private static Dictionary<DateTime, int> Agrupar(IDictionary<DateTime, int> porDia)
            => (porDia ?? new Dictionary<DateTime, int>()).GroupBy(p => p.Key.Date).ToDictionary(g => g.Key, g => g.Sum(p => p.Value));
    }

    /// <summary>
    /// NestoAPI#896: construcción de los rasgos del modelo de impagados. PURA. Regla de oro: ningún rasgo usa nada posterior al giro:
    /// solo recibos del cliente girados ANTES de ese día y solo devoluciones cuyo impagado tiene fecha ANTERIOR a ese día (un recibo
    /// anterior que volverá después cuenta ese día como no devuelto: es lo que se sabía).
    /// </summary>
    public static class ConstructorRasgosImpagos
    {
        /// <summary>Días que se espera a que vuelva un recibo (la etiqueta) y que tarda un giro en poder entrenar.</summary>
        public const int DIAS_ETIQUETA = 45;

        internal static string Clave(string empresa, string cliente) => $"{empresa?.Trim()}|{cliente?.Trim()}".ToUpperInvariant();

        /// <summary>
        /// Un ejemplo por recibo girado hasta <paramref name="hoy"/> − <see cref="DIAS_ETIQUETA"/> días (los más recientes aún pueden
        /// volver) y desde <paramref name="desde"/> (los primeros no tienen historia detrás), en orden de giro. Los recibos deben venir
        /// ya enlazados (<see cref="EnlazadorImpagados"/>).
        /// </summary>
        public static List<EjemploImpago> Construir(IEnumerable<ReciboGirado> recibos, NivelGeneralImpagos general, DateTime hoy, DateTime? desde = null)
        {
            DateTime corte = hoy.Date.AddDays(-DIAS_ETIQUETA);
            var ejemplos = new List<EjemploImpago>();
            foreach (IGrouping<string, ReciboGirado> grupo in (recibos ?? Enumerable.Empty<ReciboGirado>())
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Cliente)).GroupBy(r => r.ClaveCliente))
            {
                List<ReciboGirado> delCliente = grupo.OrderBy(r => r.FechaGiro).ThenBy(r => r.NumOrden).ToList();
                foreach (ReciboGirado recibo in delCliente.Where(r => r.FechaGiro.Date <= corte && (!desde.HasValue || r.FechaGiro.Date >= desde.Value.Date)))
                {
                    ejemplos.Add(new EjemploImpago
                    {
                        Recibo = recibo,
                        Devuelto = recibo.Devuelto,
                        Rasgos = Rasgos(recibo, recibo.FechaGiro, delCliente, general)
                    });
                }
            }
            return ejemplos.OrderBy(e => e.FechaGiro).ThenBy(e => e.Recibo.Empresa).ThenBy(e => e.Recibo.NumOrden).ToList();
        }

        /// <summary>
        /// Los rasgos de <paramref name="recibo"/> si se gira el día <paramref name="diaGiro"/>. <paramref name="delCliente"/> puede traer
        /// cualquier cosa del cliente (recibos posteriores, el propio recibo, devoluciones posteriores): solo se mira lo anterior a ese día.
        /// </summary>
        public static RasgosImpago Rasgos(ReciboGirado recibo, DateTime diaGiro, IEnumerable<ReciboGirado> delCliente, NivelGeneralImpagos general)
        {
            DateTime dia = diaGiro.Date;
            List<ReciboGirado> anteriores = (delCliente ?? Enumerable.Empty<ReciboGirado>())
                .Where(r => r != null && r.FechaGiro.Date < dia && !(r.NumOrden == recibo.NumOrden && Igual(r.Empresa, recibo.Empresa)))
                .ToList();
            // Devoluciones que ese día ya se conocían (el impagado es de un día anterior).
            List<ReciboGirado> devueltos = anteriores.Where(r => r.FechaImpago.HasValue && r.FechaImpago.Value.Date < dia).ToList();
            ReciboGirado ultima = devueltos.OrderByDescending(r => r.FechaImpago.Value).ThenByDescending(r => r.NumOrden).FirstOrDefault();
            List<double> importes = anteriores.Select(r => r.Importe).Where(i => i > 0).OrderBy(i => i).ToList();
            double mediana = importes.Count == 0 ? 0 : importes[(importes.Count - 1) / 2];
            string ccc = recibo.Ccc?.Trim() ?? string.Empty;

            return new RasgosImpago
            {
                MotivoUltima = ultima == null ? MotivosImpago.NINGUNA : MotivosImpago.Clasificar(ultima.MotivoImpago),
                FormaVenta = Categoria(recibo.FormaVenta),
                Vendedor = Categoria(recibo.Vendedor),
                Entidad = Categoria(recibo.Entidad),
                DiasDesdeUltimaDevolucion = ultima == null ? -1 : (dia - ultima.FechaImpago.Value.Date).Days,
                Devoluciones3Meses = devueltos.Count(r => (dia - r.FechaImpago.Value.Date).Days <= 90),
                Devoluciones12Meses = devueltos.Count(r => (dia - r.FechaImpago.Value.Date).Days <= 365),
                DevolucionesTotales = devueltos.Count,
                RecibosAnteriores = anteriores.Count,
                Recibos12Meses = anteriores.Count(r => (dia - r.FechaGiro.Date).Days <= 365),
                TasaDevolucionCliente = anteriores.Count == 0 ? -1 : (float)devueltos.Count / anteriores.Count,
                AntiguedadDias = anteriores.Count == 0 ? -1 : (dia - anteriores.Min(r => r.FechaGiro.Date)).Days,
                RecibosUltimos45 = anteriores.Count(r => (dia - r.FechaGiro.Date).Days <= DIAS_ETIQUETA && !(r.FechaImpago.HasValue && r.FechaImpago.Value.Date < dia)),
                Importe = (float)Math.Max(0, recibo.Importe),
                RelacionImporteHabitual = mediana > 0 ? (float)Math.Min(100, recibo.Importe / mediana) : -1,
                EsRegiro = devueltos.Any(r => r.ClaveEfecto == recibo.ClaveEfecto) ? 1 : 0,
                PrimerGiroCuenta = anteriores.Any(r => Igual(r.Ccc, ccc)) ? 0 : 1,
                NumeroEfecto = int.TryParse(recibo.Efecto?.Trim(), out int efecto) && efecto > 0 ? Math.Min(efecto, 99) : 0,
                DiasFacturaAGiro = Math.Max(0, (dia - recibo.FechaFactura.Date).Days),
                DiaMes = dia.Day,
                Mes = dia.Month,
                DiaSemana = (int)dia.DayOfWeek,
                NivelGeneral14 = general?.Nivel14(dia) ?? -1,
                NivelGeneral30 = general?.Nivel30(dia) ?? -1
            };
        }

        private static string Categoria(string valor) => string.IsNullOrWhiteSpace(valor) ? MapaCategoriasImpagos.OTROS : valor.Trim().ToUpperInvariant();

        private static bool Igual(string a, string b) => string.Equals(a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// NestoAPI#896: las categorías con valor propio en el modelo (vendedores y entidades con al menos <see cref="MINIMO"/> recibos de
    /// entrenamiento); las demás van juntas como <see cref="OTROS"/>. Se guarda con el modelo para aplicar lo mismo al predecir.
    /// </summary>
    public class MapaCategoriasImpagos
    {
        public const string OTROS = "(otros)";
        public const int MINIMO = 200;

        public List<string> Vendedores { get; set; } = new List<string>();
        public List<string> Entidades { get; set; } = new List<string>();
        public List<string> FormasVenta { get; set; } = new List<string>();

        public static MapaCategoriasImpagos Desde(IEnumerable<EjemploImpago> ejemplos, int minimo = MINIMO)
        {
            List<RasgosImpago> rasgos = (ejemplos ?? Enumerable.Empty<EjemploImpago>()).Select(e => e.Rasgos).ToList();
            List<string> Frecuentes(Func<RasgosImpago, string> campo) => rasgos.GroupBy(campo, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= minimo && g.Key != OTROS).Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            return new MapaCategoriasImpagos
            {
                Vendedores = Frecuentes(r => r.Vendedor),
                Entidades = Frecuentes(r => r.Entidad),
                FormasVenta = Frecuentes(r => r.FormaVenta)
            };
        }

        /// <summary>Copia de los rasgos con las categorías poco frecuentes (o desconocidas) como <see cref="OTROS"/>.</summary>
        public RasgosImpago Aplicar(RasgosImpago r) => Copiar(r, new RasgosImpago());

        public FilaImpago Fila(EjemploImpago e)
        {
            var fila = (FilaImpago)Copiar(e.Rasgos, new FilaImpago());
            fila.Label = e.Devuelto;
            return fila;
        }

        private RasgosImpago Copiar(RasgosImpago r, RasgosImpago destino)
        {
            destino.MotivoUltima = string.IsNullOrWhiteSpace(r.MotivoUltima) ? MotivosImpago.NINGUNA : r.MotivoUltima;
            destino.FormaVenta = Conocida(FormasVenta, r.FormaVenta);
            destino.Vendedor = Conocida(Vendedores, r.Vendedor);
            destino.Entidad = Conocida(Entidades, r.Entidad);
            destino.DiasDesdeUltimaDevolucion = r.DiasDesdeUltimaDevolucion;
            destino.Devoluciones3Meses = r.Devoluciones3Meses;
            destino.Devoluciones12Meses = r.Devoluciones12Meses;
            destino.DevolucionesTotales = r.DevolucionesTotales;
            destino.RecibosAnteriores = r.RecibosAnteriores;
            destino.Recibos12Meses = r.Recibos12Meses;
            destino.TasaDevolucionCliente = r.TasaDevolucionCliente;
            destino.AntiguedadDias = r.AntiguedadDias;
            destino.RecibosUltimos45 = r.RecibosUltimos45;
            destino.Importe = r.Importe;
            destino.RelacionImporteHabitual = r.RelacionImporteHabitual;
            destino.EsRegiro = r.EsRegiro;
            destino.PrimerGiroCuenta = r.PrimerGiroCuenta;
            destino.NumeroEfecto = r.NumeroEfecto;
            destino.DiasFacturaAGiro = r.DiasFacturaAGiro;
            destino.DiaMes = r.DiaMes;
            destino.Mes = r.Mes;
            destino.DiaSemana = r.DiaSemana;
            destino.NivelGeneral14 = r.NivelGeneral14;
            destino.NivelGeneral30 = r.NivelGeneral30;
            return destino;
        }

        private static string Conocida(List<string> conocidas, string valor)
            => valor != null && conocidas != null && conocidas.Contains(valor.Trim(), StringComparer.OrdinalIgnoreCase) ? valor.Trim().ToUpperInvariant() : OTROS;
    }
}
