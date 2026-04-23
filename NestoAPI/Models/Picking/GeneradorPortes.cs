using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    public class GeneradorPortes
    {
        private readonly NVEntities db;
        private readonly PedidoPicking pedido;
        private readonly GestorPedidosVenta gestorPedidos;

        public GeneradorPortes(NVEntities db, PedidoPicking pedido)
            : this(db, pedido, new GestorPedidosVenta(new ServicioPedidosVenta()))
        {
        }

        // Constructor para tests: se puede inyectar un GestorPedidosVenta construido
        // con un IServicioPedidosVenta mockeado para aislarnos de la BD real.
        internal GeneradorPortes(NVEntities db, PedidoPicking pedido, GestorPedidosVenta gestorPedidos)
        {
            this.db = db;
            this.pedido = pedido;
            this.gestorPedidos = gestorPedidos;
        }

        public void Ejecutar()
        {
            AnadirLineaPortesSiNoExiste();
            AnadirLineaComisionReembolsoSiProcede();
        }

        private void AnadirLineaPortesSiNoExiste()
        {
            // Delegamos el cálculo a GestorPortes (lógica centralizada)
            bool esProvincial = GestorPortes.EsProvincial(pedido.CodigoPostal);
            string cuenta = esProvincial
                ? Constantes.Cuentas.CUENTA_PORTES_ONTIME
                : Constantes.Cuentas.CUENTA_PORTES_CEX;
            decimal portes = esProvincial
                ? Constantes.Portes.PROVINCIAL
                : Constantes.Portes.PENINSULAR;

            // Si ya tiene portes, no los volvemos a añadir
            LinPedidoVta lineaPortes = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == pedido.Empresa && l.Número == pedido.Id && l.Producto != null && l.Producto.Trim() == cuenta && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO);
            if (lineaPortes != null)
            {
                return;
            }

            LinPedidoVta lineaVta = gestorPedidos.CrearLineaVta(pedido.Empresa, pedido.Id, PedidosVentaController.TIPO_LINEA_CUENTA_CONTABLE, cuenta, 1, portes, "");
            db.LinPedidoVtas.Add(lineaVta);
            pedido.Lineas.Add(new LineaPedidoPicking
            {
                Id = 0,
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = portes,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = cuenta,
                FechaEntrega = DateTime.Today
            });
        }

        // NestoAPI#174: si el pedido es contra reembolso y no queda ninguna línea de
        // comisión sin picking asignado (porque ya se facturó en un picking anterior
        // o nunca se creó), añadimos una nueva. El picking actual le asignará el nº
        // y se facturará en este ciclo, igual que los portes. Cubre el caso del split
        // de envíos, donde cada envío debe llevar su propia comisión.
        private void AnadirLineaComisionReembolsoSiProcede()
        {
            CabPedidoVta cabecera = db.CabPedidoVtas
                .FirstOrDefault(c => c.Empresa == pedido.Empresa && c.Número == pedido.Id);
            if (cabecera == null)
            {
                return;
            }

            bool esContraReembolso = GestorPortes.EsContraReembolso(
                cabecera.Forma_Pago,
                cabecera.PlazosPago,
                cabecera.CCC,
                cabecera.Periodo_Facturacion,
                cabecera.NotaEntrega);

            string cuentaReembolso = Constantes.Cuentas.CUENTA_PORTES_VENTA_GENERAL;
            // Identificamos la línea por su texto (lo mismo que hace
            // GestorPortes.GestionarLineasPortesYReembolso). La cuenta es la misma que
            // la de portes, por eso hace falta un filtro adicional por texto.
            // Picking == null indica que la línea aún no ha sido enganchada a un
            // picking concreto y por tanto sigue pendiente de facturar.
            bool yaHayComisionSinPicking = db.LinPedidoVtas.Any(l =>
                l.Empresa == pedido.Empresa
                && l.Número == pedido.Id
                && l.Producto != null
                && l.Producto.Trim() == cuentaReembolso
                && l.Texto != null
                && l.Texto.Contains("reembolso")
                && l.Picking == null);

            if (!DebeAnadirComisionReembolso(
                    esContraReembolso,
                    cabecera.NoCobrarComisionReembolso,
                    GestorPortes.FlagNoCobrarReembolsoEsEfectivo(),
                    yaHayComisionSinPicking))
            {
                return;
            }

            LinPedidoVta lineaComision = gestorPedidos.CrearLineaVta(
                pedido.Empresa,
                pedido.Id,
                PedidosVentaController.TIPO_LINEA_CUENTA_CONTABLE,
                cuentaReembolso,
                1,
                Constantes.Portes.INCREMENTO_REEMBOLSO,
                "");
            // CrearLineaVta usa por defecto el Concepto del plan contable; sobreescribimos
            // con el texto canónico para que futuras búsquedas por "reembolso" la detecten.
            lineaComision.Texto = Constantes.Portes.TEXTO_COMISION_REEMBOLSO;
            db.LinPedidoVtas.Add(lineaComision);
            pedido.Lineas.Add(new LineaPedidoPicking
            {
                Id = 0,
                Cantidad = 1,
                CantidadReservada = 1,
                BaseImponible = Constantes.Portes.INCREMENTO_REEMBOLSO,
                TipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                Producto = cuentaReembolso,
                FechaEntrega = DateTime.Today
            });
        }

        /// <summary>
        /// Regla de negocio pura sobre cuándo añadir una línea de comisión por contra
        /// reembolso en picking. Aislada como método estático para poder cubrirla con
        /// tests unitarios sin arrastrar la dependencia de BD de GeneradorPortes.
        /// </summary>
        internal static bool DebeAnadirComisionReembolso(
            bool esContraReembolso,
            bool noCobrarComisionReembolso,
            bool flagNoCobrarEsEfectivo,
            bool yaHayLineaComisionSinPicking)
        {
            if (!esContraReembolso) return false;
            if (noCobrarComisionReembolso && flagNoCobrarEsEfectivo) return false;
            if (yaHayLineaComisionSinPicking) return false;
            return true;
        }
    }
}
