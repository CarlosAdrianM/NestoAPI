using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    public class ResultadoGestionPortes
    {
        public bool Modificado { get; set; }
        public decimal ImportePortesEliminados { get; set; }
    }

    public class ResultadoPortes
    {
        public decimal ImportePortes { get; set; }
        public decimal ComisionReembolso { get; set; }
        public decimal ImporteMinimoPedidoSinPortes { get; set; }
        public decimal ImporteActualPedido { get; set; }
        public decimal ImporteFaltaParaPortesGratis { get; set; }
        public bool PortesGratis { get; set; }
        public bool EsContraReembolso { get; set; }
        public string CuentaPortes { get; set; }
        public string CuentaReembolso { get; set; }
    }

    public class PedidoPortesInput
    {
        public string CodigoPostal { get; set; }
        public string Ruta { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public string CCC { get; set; }
        public string PeriodoFacturacion { get; set; }
        public bool NotaEntrega { get; set; }
        public bool EsCanalExterno { get; set; }
        public bool EsPrecioPublicoFinal { get; set; }
        public string Iva { get; set; }
        public decimal BaseImponibleProductos { get; set; }
        public bool AnadirPortes { get; set; } = true;

        /// <summary>
        /// Issue #159: si es <c>true</c> y la fecha actual es anterior a
        /// <see cref="Constantes.Pedidos.FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO"/>,
        /// el cálculo de comisión por contra reembolso se fuerza a 0. A partir de esa
        /// fecha el flag se ignora. Para testear el corte sin congelar el reloj, el
        /// cálculo respeta <see cref="GestorPortes.FechaActualParaPruebas"/>.
        /// </summary>
        public bool NoCobrarComisionReembolso { get; set; }
    }

    public class GestorPortes
    {
        /// <summary>
        /// Issue #159: permite a los tests fijar una "fecha actual" distinta de <see cref="DateTime.Now"/>
        /// para validar el comportamiento alrededor de la fecha de corte
        /// <see cref="Constantes.Pedidos.FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO"/>.
        /// En producción siempre es <c>null</c>.
        /// </summary>
        internal static DateTime? FechaActualParaPruebas { get; set; }

        /// <summary>
        /// Issue #159: override del incremento de reembolso para tests. En producción la
        /// constante <see cref="Constantes.Portes.INCREMENTO_REEMBOLSO"/> está a 0 (fase de
        /// solo-visibilidad), pero los tests necesitan simular un valor distinto de cero
        /// para validar el efecto del flag <c>NoCobrarComisionReembolso</c>.
        /// En producción siempre es <c>null</c>.
        /// </summary>
        internal static decimal? IncrementoReembolsoParaPruebas { get; set; }

        private static DateTime FechaActual() => FechaActualParaPruebas ?? DateTime.Now;

        private static decimal IncrementoReembolso()
            => IncrementoReembolsoParaPruebas ?? Constantes.Portes.INCREMENTO_REEMBOLSO;

        /// <summary>
        /// Issue #159: devuelve si hoy debemos honrar el flag <c>NoCobrarComisionReembolso</c>.
        /// Antes de <see cref="Constantes.Pedidos.FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO"/>
        /// el flag es efectivo; a partir de esa fecha se ignora.
        /// </summary>
        internal static bool FlagNoCobrarReembolsoEsEfectivo()
            => FechaActual() < Constantes.Pedidos.FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO;

        /// <summary>
        /// Determina si un pedido es contra reembolso basándose en los datos de la cabecera.
        /// Centraliza la lógica que antes estaba duplicada en GestorPedidosVenta.ImporteReembolso()
        /// y GestorEnviosAgencia.ImporteReembolso().
        /// </summary>
        public static bool EsContraReembolso(string formaPago, string plazosPago,
            string ccc, string periodoFacturacion, bool notaEntrega)
        {
            if (ccc != null)
                return false;

            if (periodoFacturacion == Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES)
                return false;

            if (formaPago == "CNF" ||
                formaPago == Constantes.FormasPago.TRANSFERENCIA ||
                formaPago == "CHC" ||
                formaPago == Constantes.FormasPago.TARJETA)
                return false;

            if (notaEntrega)
                return false;

            if (!string.IsNullOrWhiteSpace(plazosPago) && plazosPago.Trim() == Constantes.PlazosPago.PREPAGO)
                return false;

            return true;
        }

        /// <summary>
        /// Calcula portes y comisión de reembolso de forma pura (sin acceso a BD).
        /// </summary>
        public static ResultadoPortes CalcularPortes(PedidoPortesInput input)
        {
            var resultado = new ResultadoPortes
            {
                ImporteActualPedido = input.BaseImponibleProductos
            };

            // Tienda online y Glovo no llevan portes ni reembolso
            if (input.EsCanalExterno || input.Ruta == Constantes.Pedidos.RUTA_GLOVO)
            {
                resultado.PortesGratis = true;
                resultado.ImporteFaltaParaPortesGratis = 0;
                return resultado;
            }

            // Nota de entrega no lleva portes ni reembolso
            if (input.NotaEntrega)
            {
                resultado.PortesGratis = true;
                resultado.ImporteFaltaParaPortesGratis = 0;
                return resultado;
            }

            // Si no se deben añadir portes (ej: almacén REI/ALC, o desactivado manualmente)
            if (!input.AnadirPortes)
            {
                resultado.PortesGratis = true;
                resultado.ImporteFaltaParaPortesGratis = 0;
                return resultado;
            }

            // Sin productos no tiene sentido cobrar portes ni reembolso,
            // pero calculamos umbral e importe para que los clientes puedan
            // usarlos en comparaciones locales cuando se añadan productos.
            if (input.BaseImponibleProductos <= 0 && GestorImportesMinimos.esRutaConPortes(input.Ruta))
            {
                resultado.PortesGratis = true;
                resultado.ImporteMinimoPedidoSinPortes = ObtenerUmbralPortesGratis(
                    input.CodigoPostal, input.EsPrecioPublicoFinal, input.Iva);
                resultado.ImporteFaltaParaPortesGratis = resultado.ImporteMinimoPedidoSinPortes;
                if (EsProvincial(input.CodigoPostal))
                {
                    resultado.ImportePortes = Constantes.Portes.PROVINCIAL;
                    resultado.CuentaPortes = Constantes.Cuentas.CUENTA_PORTES_ONTIME;
                }
                else
                {
                    resultado.ImportePortes = Constantes.Portes.PENINSULAR;
                    resultado.CuentaPortes = Constantes.Cuentas.CUENTA_PORTES_CEX;
                }
                return resultado;
            }
            else if (input.BaseImponibleProductos <= 0)
            {
                resultado.PortesGratis = true;
                return resultado;
            }

            // Comisión contra reembolso (independiente de la ruta).
            // Si IVA=null, NestoAPI reseteará al guardar CCC/FormaPago/PlazosPago/PeriodoFacturacion
            // a (null, EFC, CONTADO, NRM) en PutPedidoVenta. El cálculo de portes debe hacer el mismo
            // supuesto para que los clientes que aún no tienen IVA asignado (ej. NestoApp antes de
            // completar cliente) reciban un resultado coherente con lo que se persistirá.
            string formaPagoEfectiva = input.FormaPago;
            string plazosPagoEfectivo = input.PlazosPago;
            string cccEfectivo = input.CCC;
            string periodoFacturacionEfectivo = input.PeriodoFacturacion;
            if (input.Iva == null)
            {
                formaPagoEfectiva = Constantes.FormasPago.EFECTIVO;
                plazosPagoEfectivo = Constantes.PlazosPago.CONTADO;
                cccEfectivo = null;
                periodoFacturacionEfectivo = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL;
            }
            resultado.EsContraReembolso = EsContraReembolso(
                formaPagoEfectiva, plazosPagoEfectivo,
                cccEfectivo, periodoFacturacionEfectivo, input.NotaEntrega);

            decimal incrementoReembolso = IncrementoReembolso();
            if (resultado.EsContraReembolso && incrementoReembolso > 0
                && !(input.NoCobrarComisionReembolso && FlagNoCobrarReembolsoEsEfectivo()))
            {
                resultado.ComisionReembolso = incrementoReembolso;
                resultado.CuentaReembolso = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL;
            }

            // Rutas sin portes: no llevan portes pero sí pueden llevar reembolso
            if (!GestorImportesMinimos.esRutaConPortes(input.Ruta))
            {
                resultado.PortesGratis = true;
                resultado.ImporteFaltaParaPortesGratis = 0;
                return resultado;
            }

            // Determinar umbral según tipo de pedido
            decimal umbralPortesGratis = ObtenerUmbralPortesGratis(
                input.CodigoPostal, input.EsPrecioPublicoFinal, input.Iva);
            resultado.ImporteMinimoPedidoSinPortes = umbralPortesGratis;
            resultado.PortesGratis = input.BaseImponibleProductos >= umbralPortesGratis;
            resultado.ImporteFaltaParaPortesGratis = resultado.PortesGratis
                ? 0
                : umbralPortesGratis - input.BaseImponibleProductos;

            if (!resultado.PortesGratis)
            {
                // Determinar importe y cuenta según código postal
                if (EsProvincial(input.CodigoPostal))
                {
                    resultado.ImportePortes = Constantes.Portes.PROVINCIAL;
                    resultado.CuentaPortes = Constantes.Cuentas.CUENTA_PORTES_ONTIME;
                }
                else
                {
                    resultado.ImportePortes = Constantes.Portes.PENINSULAR;
                    resultado.CuentaPortes = Constantes.Cuentas.CUENTA_PORTES_CEX;
                }
            }

            return resultado;
        }

        /// <summary>
        /// Determina si el código postal corresponde a envío provincial (más barato).
        /// Provincias: Madrid (28), Guadalajara (19), Toledo (45).
        /// </summary>
        public static bool EsProvincial(string codigoPostal)
        {
            if (string.IsNullOrEmpty(codigoPostal))
                return false;

            return codigoPostal.StartsWith("28") ||
                   codigoPostal.StartsWith("19") ||
                   codigoPostal.StartsWith("45");
        }

        public static bool EsBaleares(string codigoPostal)
        {
            if (string.IsNullOrEmpty(codigoPostal))
                return false;

            return codigoPostal.StartsWith("07");
        }

        public static bool EsCanarias(string codigoPostal)
        {
            if (string.IsNullOrEmpty(codigoPostal))
                return false;

            return codigoPostal.StartsWith("35") ||
                   codigoPostal.StartsWith("38");
        }

        /// <summary>
        /// Obtiene el umbral de importe a partir del cual los portes son gratis,
        /// teniendo en cuenta el tipo de pedido y la zona geográfica.
        /// Provincial (28, 19, 45): 75€, Peninsular: 100€, Baleares: 150€, Canarias: 400€.
        /// Los casos de canal externo y Glovo se gestionan antes (early return con PortesGratis=true).
        /// </summary>
        public static decimal ObtenerUmbralPortesGratis(string codigoPostal,
            bool esPrecioPublicoFinal = false, string iva = "")
        {
            // Espejo: IVA vacío/null → umbral más alto
            if (string.IsNullOrEmpty(iva))
            {
                return GestorImportesMinimos.IMPORTE_MINIMO_ESPEJO;
            }

            // Precio público final (caso especial de tienda online que sí pasa por aquí)
            if (esPrecioPublicoFinal)
            {
                return GestorImportesMinimos.IMPORTE_MINIMO_TIENDA_ONLINE_PRECIO_PUBLICO_FINAL;
            }

            // Código postal desconocido: devolver el umbral más alto (Canarias).
            // El cliente debe hacer una nueva llamada cuando disponga del CP real.
            if (string.IsNullOrEmpty(codigoPostal))
            {
                return GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS;
            }

            // Por zona geográfica
            if (EsCanarias(codigoPostal))
            {
                return GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS;
            }

            if (EsBaleares(codigoPostal))
            {
                return GestorImportesMinimos.IMPORTE_MINIMO_BALEARES;
            }

            if (EsProvincial(codigoPostal))
            {
                return GestorImportesMinimos.IMPORTE_MINIMO;
            }

            // Resto peninsular
            return GestorImportesMinimos.IMPORTE_MINIMO_PENINSULAR;
        }

        /// <summary>
        /// Gestiona las líneas de portes y comisión reembolso en un pedido DTO.
        /// Añade o quita líneas según corresponda.
        /// Devuelve true si se modificaron las líneas.
        /// </summary>
        public static ResultadoGestionPortes GestionarLineasPortes(
            ICollection<Models.PedidosVenta.LineaPedidoVentaDTO> lineas,
            ResultadoPortes resultado,
            string iva,
            IEnumerable<Models.PedidosBase.ParametrosIvaBase> parametrosIva)
        {
            var resultadoGestion = new ResultadoGestionPortes();

            // Buscar líneas existentes de portes (cuentas 624xxx). Excluimos las líneas cuyo
            // texto contiene "reembolso" porque la cuenta de comisión reembolso también empieza
            // por 624 y, sin este filtro, se confundiría con una línea de portes (issue #159).
            var lineaPortesExistente = lineas.FirstOrDefault(l =>
                l.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE &&
                l.Producto != null &&
                l.Producto.Trim().StartsWith("624") &&
                !(l.texto != null && l.texto.IndexOf("reembolso", StringComparison.OrdinalIgnoreCase) >= 0));

            // Buscar línea existente de comisión reembolso
            var lineaReembolsoExistente = lineas.FirstOrDefault(l =>
                l.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE &&
                l.Producto != null &&
                l.Producto.Trim() == Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL &&
                l.texto != null &&
                l.texto.IndexOf("reembolso", StringComparison.OrdinalIgnoreCase) >= 0);

            // Gestionar línea de portes
            if (resultado.ImportePortes > 0 && !resultado.PortesGratis)
            {
                if (lineaPortesExistente == null)
                {
                    var lineaReferencia = lineas.FirstOrDefault();
                    if (lineaReferencia != null)
                    {
                        var lineaPortes = CrearLineaPortes(resultado, lineaReferencia, iva, parametrosIva);
                        lineas.Add(lineaPortes);
                        resultadoGestion.Modificado = true;
                    }
                }
            }
            else if (lineaPortesExistente != null && lineaPortesExistente.id == 0 &&
                     !Constantes.FormasVenta.EsCanalExterno(lineaPortesExistente.formaVenta))
            {
                // Solo quitar si es línea nueva (id == 0) y no viene de un canal externo.
                // Los canales externos (Amazon, TiendaOnline, etc.) gestionan sus propios portes.
                resultadoGestion.ImportePortesEliminados = lineaPortesExistente.PrecioUnitario;
                lineas.Remove(lineaPortesExistente);
                resultadoGestion.Modificado = true;
            }

            // Gestionar línea de comisión reembolso
            if (resultado.ComisionReembolso > 0 && resultado.EsContraReembolso)
            {
                if (lineaReembolsoExistente == null)
                {
                    var lineaReferencia = lineas.FirstOrDefault();
                    if (lineaReferencia != null)
                    {
                        var lineaReembolso = CrearLineaReembolso(resultado, lineaReferencia, iva, parametrosIva);
                        lineas.Add(lineaReembolso);
                        resultadoGestion.Modificado = true;
                    }
                }
            }
            else if (lineaReembolsoExistente != null && lineaReembolsoExistente.id == 0)
            {
                lineas.Remove(lineaReembolsoExistente);
                resultadoGestion.Modificado = true;
            }

            return resultadoGestion;
        }

        private static Models.PedidosVenta.LineaPedidoVentaDTO CrearLineaPortes(
            ResultadoPortes resultado,
            Models.PedidosVenta.LineaPedidoVentaDTO lineaReferencia,
            string iva,
            IEnumerable<Models.PedidosBase.ParametrosIvaBase> parametrosIva)
        {
            var linea = new Models.PedidosVenta.LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                almacen = lineaReferencia.almacen,
                Producto = resultado.CuentaPortes,
                Cantidad = 1,
                delegacion = lineaReferencia.delegacion,
                formaVenta = lineaReferencia.formaVenta,
                estado = lineaReferencia.estado,
                texto = "Portes",
                PrecioUnitario = resultado.ImportePortes,
                iva = iva,
                vistoBueno = true,
                usuario = lineaReferencia.usuario,
                fechaEntrega = lineaReferencia.fechaEntrega
            };

            AsignarIva(linea, iva, parametrosIva);
            return linea;
        }

        private static Models.PedidosVenta.LineaPedidoVentaDTO CrearLineaReembolso(
            ResultadoPortes resultado,
            Models.PedidosVenta.LineaPedidoVentaDTO lineaReferencia,
            string iva,
            IEnumerable<Models.PedidosBase.ParametrosIvaBase> parametrosIva)
        {
            var linea = new Models.PedidosVenta.LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                almacen = lineaReferencia.almacen,
                Producto = resultado.CuentaReembolso,
                Cantidad = 1,
                delegacion = lineaReferencia.delegacion,
                formaVenta = lineaReferencia.formaVenta,
                estado = lineaReferencia.estado,
                texto = "Comisión contra reembolso",
                PrecioUnitario = resultado.ComisionReembolso,
                iva = iva,
                vistoBueno = true,
                usuario = lineaReferencia.usuario,
                fechaEntrega = lineaReferencia.fechaEntrega
            };

            AsignarIva(linea, iva, parametrosIva);
            return linea;
        }

        private static void AsignarIva(
            Models.PedidosVenta.LineaPedidoVentaDTO linea,
            string iva,
            IEnumerable<Models.PedidosBase.ParametrosIvaBase> parametrosIva)
        {
            if (parametrosIva != null && parametrosIva.Any() && !string.IsNullOrEmpty(iva))
            {
                var parametro = parametrosIva.SingleOrDefault(p => p.CodigoIvaProducto == iva.Trim());
                if (parametro != null)
                {
                    linea.PorcentajeIva = parametro.PorcentajeIvaProducto;
                    linea.PorcentajeRecargoEquivalencia = parametro.PorcentajeRecargoEquivalencia;
                }
            }
        }

        /// <summary>
        /// Calcula la base imponible de solo productos (excluyendo líneas de cuentas contables tipo portes).
        /// </summary>
        public static decimal CalcularBaseImponibleProductos(IEnumerable<Models.PedidosVenta.LineaPedidoVentaDTO> lineas)
        {
            return lineas
                .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO ||
                           (l.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE &&
                            l.Producto != null &&
                            !l.Producto.Trim().StartsWith("624") &&
                            l.Producto.Trim() != Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL))
                .Sum(l => l.BaseImponible);
        }

        /// <summary>
        /// Determina si una línea es "sobre pedido" a efectos del cálculo de portes.
        /// Un producto de estado 0 nunca es sobre pedido.
        /// Un producto de estado != 0 es sobre pedido solo si no hay stock suficiente:
        /// - Con servirJunto: se mira el stock de todos los almacenes
        /// - Sin servirJunto: se mira solo el stock del almacén del pedido
        /// </summary>
        public static bool EsSobrePedidoParaPortes(short estadoProducto, int cantidadPedida,
            int stockDisponibleAlmacen, int stockDisponibleTodosAlmacenes, bool servirJunto)
        {
            if (estadoProducto == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
            {
                return false;
            }

            int stockRelevante = servirJunto ? stockDisponibleTodosAlmacenes : stockDisponibleAlmacen;
            return stockRelevante < cantidadPedida;
        }
    }
}
