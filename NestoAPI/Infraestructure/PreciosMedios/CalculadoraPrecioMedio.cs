using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Réplica EXACTA en C# del cálculo del precio medio del job de los domingos (Issue #547, corte a):
    /// <c>prdActualizarPreciosMediosDeTodosLosProductos</c> → <c>prdLLamarActualizarPrecioMedioProducto</c> →
    /// <c>prdActualizarPrecioMedioProductoNuevo</c> (operación 1).
    ///
    /// Principio (decisión de Carlos, 28/09/26): PARIDAD TOTAL con el SP, aunque sepamos que tiene fallos
    /// (abs(), medias negativas, albarán sin facturar como media anterior, primera compra que ignora el stock,
    /// truncado de money...). Cada fallo se arreglará después, escribiendo primero su test en rojo. Lo que el SP
    /// hace de forma indeterminada se resuelve aquí de forma determinista (por nº de factura) y se avisa.
    ///
    /// Pura: sin BD, sin EF, sin estado. No escribe nada.
    /// </summary>
    public static class CalculadoraPrecioMedio
    {
        /// <summary>-53690 en el SP: 01/01/1753, «el principio de los tiempos» (primer tramo).</summary>
        public static readonly DateTime FechaPrincipioDeLosTiempos = new DateTime(1753, 1, 1);

        /// <summary>2958463 en el SP: 31/12/9999 (último tramo, el de la media final).</summary>
        public static readonly DateTime FechaFinDeLosTiempos = new DateTime(9999, 12, 31);

        /// <summary>Valor por defecto de <c>#Montajes.Albaran</c> cuando no hay ninguna recepción.</summary>
        private static readonly DateTime FechaRecepcionPorDefecto = new DateTime(1990, 1, 1);

        /// <summary>Longitud de <c>@Producto char(10)</c> en <c>prdLLamarActualizarPrecioMedioProducto</c>.</summary>
        private const int LONGITUD_MAXIMA_PRODUCTO = 10;

        private const short ESTADO_FACTURADA = 4;

        public static ResultadoPrecioMedio Calcular(HistorialPrecioMedio historial)
        {
            if (historial == null)
            {
                throw new ArgumentNullException(nameof(historial));
            }
            if (historial.StockHasta == null)
            {
                throw new ArgumentException("Falta la función de stock del producto", nameof(historial));
            }

            ResultadoPrecioMedio resultado = new ResultadoPrecioMedio
            {
                Empresa = historial.Empresa,
                Producto = historial.Producto
            };

            // Orden determinista de las líneas: el cursor del SP ordena SOLO por FechaAlbarán; en los empates
            // desempatamos por nº de factura (decisión de Carlos) y después por NºOrden. Las líneas sin fecha
            // de albarán (pendientes) no entran nunca: ni en el cursor ni como «albarán anterior».
            List<LineaCompraPrecioMedio> lineas = (historial.Compras ?? new List<LineaCompraPrecioMedio>())
                .Where(l => l.FechaAlbaran.HasValue)
                .OrderBy(l => l.FechaAlbaran.Value)
                .ThenBy(l => l.NumeroFactura ?? int.MaxValue)
                .ThenBy(l => l.NumeroOrden)
                .ToList();

            MotivoPrecioMedioNoProcesado motivo = MotivoNoProcesado(historial, lineas);
            if (motivo != MotivoPrecioMedioNoProcesado.Ninguno)
            {
                resultado.Procesado = false;
                resultado.Motivo = motivo;
                if (motivo == MotivoPrecioMedioNoProcesado.CodigoDemasiadoLargo)
                {
                    resultado.Avisos.Add(new AvisoPrecioMedio(TipoAvisoPrecioMedio.CodigoDemasiadoLargo,
                        "El SP recibe @Producto char(10) y truncaría '" + historial.Producto.Trim() + "'"));
                }
                return resultado;
            }
            resultado.Procesado = true;
            resultado.Motivo = MotivoPrecioMedioNoProcesado.Ninguno;

            // Coste «vivo» de cada línea: el SP lee LinPedidoCmp.Coste de la BD, que va cambiando a medida que
            // el cursor avanza. Las líneas sin recalcular (sin facturar) conservan el coste que traigan.
            Dictionary<int, decimal?> costesVivos = new Dictionary<int, decimal?>();
            foreach (LineaCompraPrecioMedio linea in lineas)
            {
                costesVivos[linea.NumeroOrden] = linea.Coste;
            }

            AvisarEmpatesDeFacturas(lineas, resultado);

            // Una llamada a prdActualizarPrecioMedioProductoNuevo por cada (fecha de albarán, factura). El SP
            // hace una por LÍNEA, pero dos líneas de la misma factura y fecha dan dos llamadas idénticas.
            var llamadas = lineas
                .Where(l => l.NumeroFactura.HasValue)
                .Select(l => new { Fecha = l.FechaAlbaran.Value, Factura = l.NumeroFactura.Value })
                .Distinct()
                .ToList();

            foreach (var llamada in llamadas)
            {
                ProcesarFactura(historial, lineas, costesVivos, llamada.Fecha, llamada.Factura, resultado);
            }

            if (resultado.PrecioMedioFinal.HasValue && resultado.PrecioMedioFinal.Value < 0)
            {
                resultado.Avisos.Add(new AvisoPrecioMedio(TipoAvisoPrecioMedio.MediaNegativa,
                    "La media final es " + resultado.PrecioMedioFinal.Value.ToString(CultureInfo.InvariantCulture)));
            }

            return resultado;
        }

        /// <summary>
        /// Fórmula de <c>prdActualizarPrecioMedioProductoNuevo</c> (operación 1). Las reglas se aplican en el
        /// orden del SP y cada una pisa a la anterior:
        /// (a) d = |stock − cantidad| + cantidad; d ≠ 0 → (|stock − cantidad| × antiguo + base) / d; d = 0 → 0.
        /// (b) sin albarán anterior y cantidad ≠ 0 → base / cantidad (ignora el stock previo).
        /// (c) nuevo = 0, antiguo ≠ 0 y d = 0 → antiguo.
        /// (d) stock − cantidad = 0 y cantidad ≠ 0 → base / cantidad.
        /// (e) stock = 0 y cantidad = 0 → base + antiguo.
        /// Todas las divisiones truncan como <c>money</c> (<see cref="AritmeticaMoney"/>).
        /// </summary>
        public static decimal CalcularNuevoPrecioMedio(int stock, int cantidad, decimal baseImponible,
            decimal precioMedioAntiguo, bool hayAlbaranAnterior)
        {
            // OJO: abs() = el stock previo negativo se pondera como positivo (fallo conocido del SP, §1.5.1).
            int stockPrevio = Math.Abs(stock - cantidad);
            int denominador = stockPrevio + cantidad;
            decimal nuevo = 0;

            // (a)
            if (denominador != 0)
            {
                nuevo = AritmeticaMoney.Dividir((stockPrevio * precioMedioAntiguo) + baseImponible, denominador);
            }

            // (b) primera compra (fechaDesde = -53690)
            if (!hayAlbaranAnterior && cantidad != 0)
            {
                nuevo = AritmeticaMoney.Dividir(baseImponible, cantidad);
            }

            // (c)
            if (nuevo == 0 && precioMedioAntiguo != 0 && denominador == 0)
            {
                nuevo = precioMedioAntiguo;
            }

            // (d) (da lo mismo que (a), pero se replica tal cual)
            if (stock - cantidad == 0 && cantidad != 0)
            {
                nuevo = AritmeticaMoney.Dividir(baseImponible, cantidad);
            }

            // (e) línea de cantidad 0 con stock 0: suma la base entera a la media (fallo conocido, §1.5.2)
            if (stock == 0 && cantidad == 0)
            {
                nuevo = baseImponible + precioMedioAntiguo;
            }

            return nuevo;
        }

        /// <summary>Réplica del filtro de <c>prdActualizarPreciosMediosDeTodosLosProductos</c> (§1.1 a-c).</summary>
        private static MotivoPrecioMedioNoProcesado MotivoNoProcesado(HistorialPrecioMedio historial,
            List<LineaCompraPrecioMedio> lineas)
        {
            // (a) primera compra: la fecha mínima de albarán y, en ella, alguna línea facturada
            if (!lineas.Any())
            {
                return MotivoPrecioMedioNoProcesado.SinCompras;
            }
            DateTime primeraFecha = lineas[0].FechaAlbaran.Value;
            if (!lineas.Any(l => l.FechaAlbaran.Value == primeraFecha && l.NumeroFactura.HasValue))
            {
                return MotivoPrecioMedioNoProcesado.PrimeraCompraSinFacturar;
            }

            // (b)
            if (historial.Ficticio)
            {
                return MotivoPrecioMedioNoProcesado.Ficticio;
            }

            // (c) regla de montajes: «los consideramos como que no tienen compras»
            if (historial.FechaUltimoMontaje.HasValue &&
                historial.FechaUltimoMontaje.Value > (historial.FechaUltimaRecepcion ?? FechaRecepcionPorDefecto))
            {
                return MotivoPrecioMedioNoProcesado.ExcluidoPorMontaje;
            }

            // (d) @Producto char(10)
            if (historial.Producto != null && historial.Producto.Trim().Length > LONGITUD_MAXIMA_PRODUCTO)
            {
                return MotivoPrecioMedioNoProcesado.CodigoDemasiadoLargo;
            }

            return MotivoPrecioMedioNoProcesado.Ninguno;
        }

        /// <summary>Una llamada a <c>prdActualizarPrecioMedioProductoNuevo(E, espejo, factura, 1, producto, fecha)</c>.</summary>
        private static void ProcesarFactura(HistorialPrecioMedio historial, List<LineaCompraPrecioMedio> lineas,
            Dictionary<int, decimal?> costesVivos, DateTime fecha, int factura, ResultadoPrecioMedio resultado)
        {
            // 1. Cantidad y base: solo las líneas facturadas (Estado = 4) de esa factura y fecha exacta.
            List<LineaCompraPrecioMedio> lineasFactura = lineas
                .Where(l => l.FechaAlbaran.Value == fecha && l.NumeroFactura == factura && l.Estado == ESTADO_FACTURADA)
                .ToList();
            if (!lineasFactura.Any())
            {
                return; // el SP no inserta nada en la temporal y ningún UPDATE hace efecto
            }
            int cantidad = lineasFactura.Sum(l => l.Cantidad);
            decimal baseImponible = lineasFactura.Sum(l => l.BaseImponible);

            // 2. Media anterior y fecha desde
            bool hayAlbaranAnterior = BuscarMediaAnterior(lineas, costesVivos, fecha, factura, resultado,
                out decimal precioMedioAntiguo);
            DateTime fechaDesde = hayAlbaranAnterior ? fecha : FechaPrincipioDeLosTiempos;

            // 3. Fecha hasta: la siguiente fecha de albarán FACTURADA (Estado = 4) posterior a esta.
            //    (El SP no filtra TipoLínea aquí; las líneas de otro tipo no llevan código de producto.)
            DateTime fechaHasta = lineas
                .Where(l => l.Estado == ESTADO_FACTURADA && l.FechaAlbaran.Value > fecha)
                .Select(l => (DateTime?)l.FechaAlbaran.Value)
                .FirstOrDefault() ?? FechaFinDeLosTiempos;

            // 4. Stock a la fecha (incluye la propia compra)
            int stock = historial.StockHasta(fecha);

            // 5. Fórmula
            decimal nuevo = CalcularNuevoPrecioMedio(stock, cantidad, baseImponible, precioMedioAntiguo, hayAlbaranAnterior);

            // 6. Escrituras: Productos.PrecioMedio, LinPedidoCmp.Coste de TODAS las líneas de esa factura y fecha
            //    (el UPDATE del SP no filtra estado) y el tramo de ##global.
            resultado.PrecioMedioFinal = nuevo;
            foreach (LineaCompraPrecioMedio linea in lineas.Where(l => l.FechaAlbaran.Value == fecha && l.NumeroFactura == factura))
            {
                costesVivos[linea.NumeroOrden] = nuevo;
                resultado.CostesPorLinea[linea.NumeroOrden] = nuevo;
            }
            resultado.Tramos.Add(new TramoPrecioMedio
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                PrecioMedio = nuevo,
                NumeroFactura = factura,
                FechaAlbaran = fecha
            });
        }

        /// <summary>
        /// «Albarán anterior» del SP: <c>top 1 NºAlbarán</c> de las líneas del producto (cualquier estado) con
        /// fecha anterior, <c>order by FechaAlbarán desc</c>; la media anterior es <c>isnull(Coste, 0)</c> de una
        /// línea de ese albarán con fecha anterior. Devuelve false si no hay (primera compra).
        /// </summary>
        private static bool BuscarMediaAnterior(List<LineaCompraPrecioMedio> lineas, Dictionary<int, decimal?> costesVivos,
            DateTime fecha, int factura, ResultadoPrecioMedio resultado, out decimal precioMedioAntiguo)
        {
            precioMedioAntiguo = 0;
            List<LineaCompraPrecioMedio> anteriores = lineas.Where(l => l.FechaAlbaran.Value < fecha).ToList();
            if (!anteriores.Any())
            {
                return false;
            }

            // En un empate de fecha el SP coge un albarán cualquiera; aquí, el de la última línea en nuestro
            // orden (la de mayor nº de factura), que es la misma que decidió la media en esa fecha.
            LineaCompraPrecioMedio ultima = anteriores.Last();
            DateTime fechaAnterior = ultima.FechaAlbaran.Value;
            List<int?> albaranesEnEsaFecha = anteriores
                .Where(l => l.FechaAlbaran.Value == fechaAnterior)
                .Select(l => l.NumeroAlbaran)
                .Distinct()
                .ToList();
            if (albaranesEnEsaFecha.Count > 1)
            {
                resultado.Avisos.Add(new AvisoPrecioMedio(TipoAvisoPrecioMedio.EmpateAlbaranAnterior, string.Format(
                    CultureInfo.InvariantCulture, "Factura {0} del {1:dd/MM/yyyy HH:mm:ss}: {2} albaranes el {3:dd/MM/yyyy HH:mm:ss}; se usa el {4}",
                    factura, fecha, albaranesEnEsaFecha.Count, fechaAnterior, ultima.NumeroAlbaran)));
            }

            // Si el albarán es nulo, el «l.NºAlbarán = (subconsulta)» del SP no casa con nada: como si no hubiera.
            if (!ultima.NumeroAlbaran.HasValue)
            {
                return false;
            }

            // Líneas de ese albarán con fecha anterior (misma factura con dos fechas → varias). El SP coge una
            // cualquiera; en el caso real (34248) cogió la más reciente, que es la última en nuestro orden.
            List<LineaCompraPrecioMedio> lineasAlbaran = anteriores
                .Where(l => l.NumeroAlbaran == ultima.NumeroAlbaran)
                .ToList();
            LineaCompraPrecioMedio lineaMedia = lineasAlbaran.Last();
            precioMedioAntiguo = costesVivos[lineaMedia.NumeroOrden] ?? 0;

            if (lineasAlbaran.Select(l => costesVivos[l.NumeroOrden] ?? 0).Distinct().Count() > 1)
            {
                resultado.Avisos.Add(new AvisoPrecioMedio(TipoAvisoPrecioMedio.AlbaranAnteriorConVariosCostes, string.Format(
                    CultureInfo.InvariantCulture, "Factura {0} del {1:dd/MM/yyyy HH:mm:ss}: el albarán {2} tiene varios costes; se usa {3} (línea {4})",
                    factura, fecha, ultima.NumeroAlbaran, precioMedioAntiguo, lineaMedia.NumeroOrden)));
            }
            if (lineaMedia.Estado != ESTADO_FACTURADA)
            {
                resultado.Avisos.Add(new AvisoPrecioMedio(TipoAvisoPrecioMedio.AlbaranAnteriorSinFacturar, string.Format(
                    CultureInfo.InvariantCulture, "Factura {0} del {1:dd/MM/yyyy HH:mm:ss}: la media anterior ({2}) sale del albarán {3} en estado {4}",
                    factura, fecha, precioMedioAntiguo, ultima.NumeroAlbaran, lineaMedia.Estado)));
            }
            return true;
        }

        private static void AvisarEmpatesDeFacturas(List<LineaCompraPrecioMedio> lineas, ResultadoPrecioMedio resultado)
        {
            var empates = lineas
                .Where(l => l.NumeroFactura.HasValue)
                .GroupBy(l => l.FechaAlbaran.Value)
                .Select(g => new { Fecha = g.Key, Facturas = g.Select(l => l.NumeroFactura.Value).Distinct().ToList() })
                .Where(g => g.Facturas.Count > 1);
            foreach (var empate in empates)
            {
                resultado.Avisos.Add(new AvisoPrecioMedio(TipoAvisoPrecioMedio.EmpateFacturasMismaFecha, string.Format(
                    CultureInfo.InvariantCulture, "{0:dd/MM/yyyy HH:mm:ss}: facturas {1} (se procesan por nº de factura; el SP, en orden indeterminado)",
                    empate.Fecha, string.Join(", ", empate.Facturas))));
            }
        }
    }
}
