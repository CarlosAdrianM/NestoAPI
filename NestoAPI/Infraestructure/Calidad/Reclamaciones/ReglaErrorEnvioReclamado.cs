using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Reclamaciones
{
    // NestoAPI#914 (Carlos 27/11/26): «El error real es cuando decimos que hemos mandado un producto y en realidad hemos mandado otro (o
    // ninguno). ¿Cómo nos damos cuenta? Porque el cliente protesta. ¿Cómo se podría ver en los datos? Mirando si después del pedido hay una
    // rectificativa o se vuelve a enviar el mismo producto sin coste.» LA REGLA (pura) y sus datos; la lectura, en LectorReclamacionesEnvioSql.
    // La usan el corte 2 (Almacén, por preparador) y el pedido perfecto: misma regla y misma lectura. Definiciones en docs/claude/calidad-kpi.md.

    /// <summary>Qué pasó DESPUÉS de servir una línea. Cada uno se puede contar por separado.</summary>
    public enum SubMotivoReclamacion
    {
        /// <summary>«No llegó o llegó mal: se volvió a mandar»: el mismo producto, sin coste, al mismo cliente, en otro pedido o nota.</summary>
        ReenviadoSinCoste = 1,
        /// <summary>«Se mandó un producto por otro»: la línea se rectificó y se le cargó o mandó sin coste otro producto parecido.</summary>
        CambiadoPorOtro = 2,
        /// <summary>La línea se rectificó y no hubo reposición ni cambio (devolución o abono comercial). Informativo: no es del preparador.</summary>
        AbonadoSinReposicion = 3
    }

    /// <summary>Una línea servida (con albarán) que se quiere mirar.</summary>
    public class LineaServidaReclamacion
    {
        public int Pedido { get; set; }
        /// <summary>LinPedidoVta.[Nº Orden].</summary>
        public int Linea { get; set; }
        public string Producto { get; set; }
        public string Cliente { get; set; }
        /// <summary>Fecha Albarán (el día). Sin ella la línea no se puede mirar.</summary>
        public DateTime? FechaAlbaran { get; set; }
        public string Factura { get; set; }
        /// <summary>Lo servido (Cantidad − Recoger).</summary>
        public int Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string Familia { get; set; }
        public decimal? PrecioTarifa { get; set; }
        public int? Oferta { get; set; }
    }

    /// <summary>Una rectificación de una línea servida (LinFacturaVtaRectificacion por FacturaOriginalNumero + FacturaOriginalLinea).</summary>
    public class RectificacionLineaReclamacion
    {
        public int Pedido { get; set; }
        public int Linea { get; set; }
        /// <summary>La factura rectificativa (NumeroFactura).</summary>
        public string FacturaRectificativa { get; set; }
        /// <summary>Fecha de la rectificativa (CabFacturaVta.Fecha); null si no se encuentra.</summary>
        public DateTime? Fecha { get; set; }
        public decimal Cantidad { get; set; }
    }

    /// <summary>Una línea de producto posterior del mismo cliente que puede demostrar una reclamación.</summary>
    public class LineaPosteriorReclamacion
    {
        /// <summary>Es una línea de la propia rectificativa (por su Nº Factura): se le cargó o mandó al hacer la rectificativa.</summary>
        public bool EnRectificativa { get; set; }
        public string FacturaRectificativa { get; set; }
        public string Cliente { get; set; }
        public int Pedido { get; set; }
        public int Linea { get; set; }
        /// <summary>El día del pedido (CabPedidoVta.Fecha).</summary>
        public DateTime Fecha { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string Familia { get; set; }
        public decimal? PrecioTarifa { get; set; }
        /// <summary>NºOferta: la parte de una oferta (la gratis de un N+M, un regalo de oferta).</summary>
        public int? Oferta { get; set; }
        /// <summary>El producto es un regalo de Ganavisiones vigente ese día (dbo.Ganavisiones).</summary>
        public bool EsGanavisiones { get; set; }
        /// <summary>El producto es un regalo por importe de pedido vigente ese día (dbo.RegaloImportePedido).</summary>
        public bool EsRegaloPorImporte { get; set; }
        public bool NotaEntrega { get; set; }
        /// <summary>CabPedidoVta.PedidoOrigen: si es el de la línea servida, es la nota de lo pendiente (#542), no un reenvío.</summary>
        public int? PedidoOrigen { get; set; }
    }

    /// <summary>Lo leído (lo rellena <see cref="LectorReclamacionesEnvioSql"/>). Sin lógica.</summary>
    public class LecturaReclamacionesEnvio
    {
        public List<LineaServidaReclamacion> Servidas { get; set; } = new List<LineaServidaReclamacion>();
        public List<RectificacionLineaReclamacion> Rectificaciones { get; set; } = new List<RectificacionLineaReclamacion>();
        /// <summary>Líneas sin coste del MISMO producto al mismo cliente en la ventana (candidatas a reenvío).</summary>
        public List<LineaPosteriorReclamacion> MismoProducto { get; set; } = new List<LineaPosteriorReclamacion>();
        /// <summary>De las líneas rectificadas: lo de sus rectificativas y lo sin coste al cliente alrededor de ellas (candidatas a cambio).</summary>
        public List<LineaPosteriorReclamacion> OtrosProductos { get; set; } = new List<LineaPosteriorReclamacion>();
    }

    /// <summary>La línea posterior que demuestra un sub-motivo (lo que enseña el Detalle).</summary>
    public class PruebaReclamacion
    {
        public string SubMotivo { get; set; }
        public int Pedido { get; set; }
        public int Linea { get; set; }
        public DateTime Fecha { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        /// <summary>Base imponible de esa línea (0 si se mandó sin coste).</summary>
        public decimal Importe { get; set; }
        /// <summary>La rectificativa de la línea servida (en CambiadoPorOtro y AbonadoSinReposicion).</summary>
        public string FacturaRectificativa { get; set; }
        public bool EnRectificativa { get; set; }
        public bool NotaEntrega { get; set; }
    }

    /// <summary>Lo que le pasó a una línea servida después.</summary>
    public class ResultadoReclamacion
    {
        public int Pedido { get; set; }
        public int Linea { get; set; }
        public string Producto { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        /// <summary>Han pasado los días de reclamación desde el albarán: cuenta (si no, aún puede reclamar y no se evalúa).</summary>
        public bool Evaluada { get; set; }
        public HashSet<SubMotivoReclamacion> SubMotivos { get; } = new HashSet<SubMotivoReclamacion>();
        public List<PruebaReclamacion> Pruebas { get; } = new List<PruebaReclamacion>();
        /// <summary>«Error de envío reclamado»: reenviado sin coste o cambiado por otro.</summary>
        public bool ErrorReclamado => SubMotivos.Contains(SubMotivoReclamacion.ReenviadoSinCoste) || SubMotivos.Contains(SubMotivoReclamacion.CambiadoPorOtro);
    }

    /// <summary>LA REGLA (pura) de «error de envío reclamado».</summary>
    public static class ReglaErrorEnvioReclamado
    {
        /// <summary>Parámetro «(defecto)» con los días que se mira después del albarán (sin fila, <see cref="DIAS_POR_DEFECTO"/>). Leerlo no crea fila.</summary>
        public const string PARAMETRO_DIAS = "Calidad.DiasReclamacionEnvio";
        public const int DIAS_POR_DEFECTO = 45;
        public const int DIAS_MAXIMOS = 365;
        /// <summary>Lo sin coste de OTRO producto cuenta como cambio si va a ese cliente como mucho estos días antes o después de la rectificativa.</summary>
        public const int DIAS_ALREDEDOR_RECTIFICATIVA = 7;

        /// <summary>El valor del parámetro: un entero de 1 a 365; vacío, otra cosa o fuera de rango → 45.</summary>
        public static int Dias(string valor)
        {
            return int.TryParse(valor?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int dias) && dias >= 1 && dias <= DIAS_MAXIMOS
                ? dias
                : DIAS_POR_DEFECTO;
        }

        /// <summary>Sin coste: cantidad positiva y base imponible 0 (precio 0 o descuentos que suman el 100 %).</summary>
        public static bool EsSinCoste(LineaPosteriorReclamacion l) => l != null && l.Cantidad > 0 && l.BaseImponible == 0m;

        /// <summary>
        /// Un regalo, no un reenvío ni un cambio: muestra o material promocional (subgrupo MMP), parte de una oferta (NºOferta: la gratis de un
        /// N+M, regalo de oferta), regalo de Ganavisiones o regalo por importe del pedido vigentes ese día.
        /// </summary>
        public static bool EsRegalo(LineaPosteriorReclamacion l) =>
            l != null && (EsMuestra(l.SubGrupo) || (l.Oferta.HasValue && l.Oferta.Value != 0) || l.EsGanavisiones || l.EsRegaloPorImporte);

        /// <summary>La línea servida no se puede «reenviar sin coste» si ya iba gratis (base 0), si era una muestra o parte de una oferta.</summary>
        public static bool PuedeReenviarse(LineaServidaReclamacion s) =>
            s != null && s.BaseImponible > 0m && !EsMuestra(s.SubGrupo) && !(s.Oferta.HasValue && s.Oferta.Value != 0);

        /// <summary>
        /// Otro producto «parecido»: misma familia (marca) o mismo grupo y subgrupo (nunca el de muestras). Solo se usa dentro de la rectificativa
        /// de la línea o con lo mandado SIN COSTE alrededor de ella (lo cargado en pedidos normales no cuenta: sería demasiado laxo con quien
        /// compra cada semana).
        /// </summary>
        public static bool EsParecido(LineaServidaReclamacion s, LineaPosteriorReclamacion l)
        {
            if (s == null || l == null)
            {
                return false;
            }
            bool mismaFamilia = !string.IsNullOrWhiteSpace(s.Familia) && Igual(s.Familia, l.Familia);
            bool mismoSubgrupo = !string.IsNullOrWhiteSpace(s.SubGrupo) && !EsMuestra(s.SubGrupo)
                && Igual(s.Grupo, l.Grupo) && Igual(s.SubGrupo, l.SubGrupo);
            return mismaFamilia || mismoSubgrupo;
        }

        /// <summary>Lo que le pasó a cada línea servida (por pedido + línea). <paramref name="hoy"/> y <paramref name="dias"/> deciden si ya cuenta.</summary>
        public static Dictionary<Tuple<int, int>, ResultadoReclamacion> Evaluar(LecturaReclamacionesEnvio lectura, DateTime hoy, int dias)
        {
            var resultado = new Dictionary<Tuple<int, int>, ResultadoReclamacion>();
            if (lectura == null)
            {
                return resultado;
            }
            dias = dias <= 0 ? DIAS_POR_DEFECTO : dias;
            List<LineaServidaReclamacion> servidas = (lectura.Servidas ?? new List<LineaServidaReclamacion>())
                .Where(s => s != null)
                .GroupBy(s => Tuple.Create(s.Pedido, s.Linea)).Select(g => g.First()).ToList();
            foreach (LineaServidaReclamacion s in servidas)
            {
                resultado[Tuple.Create(s.Pedido, s.Linea)] = new ResultadoReclamacion
                {
                    Pedido = s.Pedido,
                    Linea = s.Linea,
                    Producto = s.Producto?.Trim(),
                    FechaAlbaran = s.FechaAlbaran?.Date,
                    Evaluada = s.FechaAlbaran.HasValue && hoy.Date >= s.FechaAlbaran.Value.Date.AddDays(dias)
                };
            }

            // 1. Reenviado sin coste: cada línea posterior demuestra UNA reclamación, la de la última vez que se le sirvió ese producto antes
            // (si el cliente lo compró dos veces en la ventana, no se cuenta dos veces).
            Dictionary<string, List<LineaServidaReclamacion>> porClienteYProducto = servidas
                .Where(s => s.FechaAlbaran.HasValue && PuedeReenviarse(s))
                .GroupBy(s => Clave(s.Cliente, s.Producto))
                .ToDictionary(g => g.Key, g => g.ToList());
            foreach (LineaPosteriorReclamacion l in Distintas(lectura.MismoProducto).Where(l => EsSinCoste(l) && !EsRegalo(l)))
            {
                if (!porClienteYProducto.TryGetValue(Clave(l.Cliente, l.Producto), out List<LineaServidaReclamacion> candidatas))
                {
                    continue;
                }
                LineaServidaReclamacion original = candidatas
                    .Where(s => s.Pedido != l.Pedido && l.PedidoOrigen != s.Pedido
                        && s.FechaAlbaran.Value.Date <= l.Fecha.Date && l.Fecha.Date <= s.FechaAlbaran.Value.Date.AddDays(dias))
                    .OrderByDescending(s => s.FechaAlbaran.Value).ThenByDescending(s => s.Pedido).ThenByDescending(s => s.Linea)
                    .FirstOrDefault();
                if (original == null)
                {
                    continue;
                }
                ResultadoReclamacion r = resultado[Tuple.Create(original.Pedido, original.Linea)];
                _ = r.SubMotivos.Add(SubMotivoReclamacion.ReenviadoSinCoste);
                r.Pruebas.Add(Prueba(SubMotivoReclamacion.ReenviadoSinCoste, l, null));
            }

            // 2. Rectificadas: cambiado por otro o abonado sin reposición
            Dictionary<Tuple<int, int>, List<RectificacionLineaReclamacion>> rectificaciones = (lectura.Rectificaciones ?? new List<RectificacionLineaReclamacion>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.FacturaRectificativa))
                .GroupBy(x => Tuple.Create(x.Pedido, x.Linea))
                .ToDictionary(g => g.Key, g => g.ToList());
            List<LineaPosteriorReclamacion> todasLasOtras = Distintas(lectura.OtrosProductos).ToList();
            // Lo que la rectificativa ABONA (cantidad negativa): si también lo carga, es una refacturación (precio, cliente), no un cambio
            Dictionary<string, HashSet<string>> abonadoEnRectificativa = todasLasOtras
                .Where(l => l.EnRectificativa && l.Cantidad < 0 && !string.IsNullOrWhiteSpace(l.FacturaRectificativa))
                .GroupBy(l => l.FacturaRectificativa.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(l => l.Producto?.Trim() ?? string.Empty), StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);
            List<LineaPosteriorReclamacion> otros = todasLasOtras
                .Where(l => l.Cantidad > 0 && !EsRegalo(l)
                    && !(l.EnRectificativa && l.FacturaRectificativa != null
                         && abonadoEnRectificativa.TryGetValue(l.FacturaRectificativa.Trim(), out HashSet<string> abonados)
                         && abonados.Contains(l.Producto?.Trim() ?? string.Empty)))
                .ToList();
            foreach (LineaServidaReclamacion s in servidas)
            {
                if (!rectificaciones.TryGetValue(Tuple.Create(s.Pedido, s.Linea), out List<RectificacionLineaReclamacion> suyas))
                {
                    continue;
                }
                ResultadoReclamacion r = resultado[Tuple.Create(s.Pedido, s.Linea)];
                foreach (LineaPosteriorReclamacion l in otros.Where(l => !Igual(l.Producto, s.Producto) && EsParecido(s, l)))
                {
                    RectificacionLineaReclamacion rectificacion = null;
                    if (l.EnRectificativa)
                    {
                        // En la misma rectificativa, cargado o sin coste
                        rectificacion = suyas.FirstOrDefault(x => Igual(x.FacturaRectificativa, l.FacturaRectificativa));
                    }
                    else if (EsSinCoste(l) && Igual(l.Cliente, s.Cliente) && l.Pedido != s.Pedido && l.PedidoOrigen != s.Pedido
                        && s.FechaAlbaran.HasValue && l.Fecha.Date >= s.FechaAlbaran.Value.Date)
                    {
                        // Sin coste, a ese cliente, alrededor de la rectificativa
                        rectificacion = suyas.FirstOrDefault(x => x.Fecha.HasValue
                            && Math.Abs((l.Fecha.Date - x.Fecha.Value.Date).TotalDays) <= DIAS_ALREDEDOR_RECTIFICATIVA);
                    }
                    if (rectificacion != null)
                    {
                        _ = r.SubMotivos.Add(SubMotivoReclamacion.CambiadoPorOtro);
                        r.Pruebas.Add(Prueba(SubMotivoReclamacion.CambiadoPorOtro, l, rectificacion.FacturaRectificativa));
                    }
                }
                if (!r.ErrorReclamado)
                {
                    _ = r.SubMotivos.Add(SubMotivoReclamacion.AbonadoSinReposicion);
                    foreach (RectificacionLineaReclamacion x in suyas.OrderBy(x => x.Fecha ?? DateTime.MaxValue))
                    {
                        r.Pruebas.Add(new PruebaReclamacion
                        {
                            SubMotivo = SubMotivoReclamacion.AbonadoSinReposicion.ToString(),
                            Pedido = s.Pedido,
                            Linea = s.Linea,
                            Fecha = x.Fecha ?? s.FechaAlbaran ?? DateTime.MinValue,
                            Producto = s.Producto?.Trim(),
                            Cantidad = (int)Math.Round(x.Cantidad, MidpointRounding.AwayFromZero),
                            Importe = 0m,
                            FacturaRectificativa = x.FacturaRectificativa?.Trim()
                        });
                    }
                }
            }
            foreach (ResultadoReclamacion r in resultado.Values)
            {
                List<PruebaReclamacion> ordenadas = r.Pruebas.OrderBy(p => p.Fecha).ThenBy(p => p.Pedido).ThenBy(p => p.Linea).ToList();
                r.Pruebas.Clear();
                r.Pruebas.AddRange(ordenadas);
            }
            return resultado;
        }

        /// <summary>
        /// De varias líneas (las de un pedido, o las de un producto de un pedido en un picking): true si alguna evaluada tiene error reclamado;
        /// false si todas se han podido mirar y ninguna lo tiene; null (no evaluado) si alguna no se puede mirar aún o no hay ninguna.
        /// <paramref name="esperadas"/>: cuántas líneas debería haber (las que faltan no tienen albarán: no evaluadas).
        /// </summary>
        public static bool? Combinar(IEnumerable<ResultadoReclamacion> lineas, int? esperadas = null)
        {
            List<ResultadoReclamacion> lista = (lineas ?? Enumerable.Empty<ResultadoReclamacion>()).Where(l => l != null).ToList();
            if (lista.Any(l => l.Evaluada && l.ErrorReclamado))
            {
                return true;
            }
            if (lista.Count == 0 || (esperadas.HasValue && lista.Count < esperadas.Value) || lista.Any(l => !l.Evaluada))
            {
                return null;
            }
            return false;
        }

        private static PruebaReclamacion Prueba(SubMotivoReclamacion subMotivo, LineaPosteriorReclamacion l, string facturaRectificativa) => new PruebaReclamacion
        {
            SubMotivo = subMotivo.ToString(),
            Pedido = l.Pedido,
            Linea = l.Linea,
            Fecha = l.Fecha,
            Producto = l.Producto?.Trim(),
            Cantidad = l.Cantidad,
            Importe = l.BaseImponible,
            FacturaRectificativa = (facturaRectificativa ?? l.FacturaRectificativa)?.Trim(),
            EnRectificativa = l.EnRectificativa,
            NotaEntrega = l.NotaEntrega
        };

        /// <summary>Una línea posterior una sola vez (la misma puede venir de la rectificativa y de los pedidos del cliente: manda la rectificativa).</summary>
        private static IEnumerable<LineaPosteriorReclamacion> Distintas(IEnumerable<LineaPosteriorReclamacion> lineas) =>
            (lineas ?? Enumerable.Empty<LineaPosteriorReclamacion>())
                .Where(l => l != null)
                .GroupBy(l => Tuple.Create(l.Pedido, l.Linea))
                .Select(g => g.OrderByDescending(l => l.EnRectificativa).First());

        private static bool EsMuestra(string subGrupo) =>
            string.Equals(subGrupo?.Trim(), Constantes.Productos.SUBGRUPO_MUESTRAS, StringComparison.OrdinalIgnoreCase);

        private static bool Igual(string a, string b) =>
            string.Equals(a?.Trim() ?? string.Empty, b?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        private static string Clave(string cliente, string producto) =>
            (cliente?.Trim().ToUpperInvariant() ?? string.Empty) + "|" + (producto?.Trim().ToUpperInvariant() ?? string.Empty);
    }
}
