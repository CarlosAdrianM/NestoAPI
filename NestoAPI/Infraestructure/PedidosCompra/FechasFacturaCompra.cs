using Elmah;
using NestoAPI.Models;
using NestoAPI.Models.PedidosCompra;
using System;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#668: fechas de compras de FechasPermitidas para un usuario (tabla fuera del EDMX).
    /// </summary>
    public class FechasPermitidasCompra
    {
        public DateTime? FechaInicialCmp { get; set; }
        public DateTime? FechaFinalCmp { get; set; }
    }

    /// <summary>
    /// NestoAPI#668: acceso a las fechas permitidas de compras. Separado para poder probar la
    /// decisión (<see cref="ComprobadorFechaFacturaCompra"/>) sin base de datos.
    /// </summary>
    internal interface IFechasPermitidasCompras
    {
        /// <summary>prdFechaEsVálida(empresa, fecha, 3 = compras, usuario) = 1.</summary>
        Task<bool> EsFechaCompraValida(string empresa, DateTime fecha, string usuario, NVEntities db);

        /// <summary>Fila de FechasPermitidas del usuario en la empresa, o null si no tiene.</summary>
        Task<FechasPermitidasCompra> LeerFechasCompra(string empresa, string usuario, NVEntities db);
    }

    internal class FechasPermitidasComprasSql : IFechasPermitidasCompras
    {
        // Mismo tipo que usa prdFechaEsVálida desde prdCrearFacturaVta (2 = ventas, 3 = compras)
        internal const int TIPO_FECHA_COMPRAS = 3;

        public async Task<bool> EsFechaCompraValida(string empresa, DateTime fecha, string usuario, NVEntities db)
        {
            var resultado = new SqlParameter("@Resultado", SqlDbType.Int) { Direction = ParameterDirection.Output };
            _ = await db.Database.ExecuteSqlCommandAsync(
                "EXEC @Resultado = prdFechaEsVálida @Empresa, @Fecha, @Tipo, @Usuario",
                resultado,
                new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa },
                new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fecha },
                new SqlParameter("@Tipo", SqlDbType.Int) { Value = TIPO_FECHA_COMPRAS },
                // Igual que lo recibe prdCrearFacturaCmp (Char 30); null = el SP decide, como allí
                new SqlParameter("@Usuario", SqlDbType.Char, 30) { Value = (object)usuario ?? DBNull.Value }
            ).ConfigureAwait(false);
            return resultado.Value != DBNull.Value && (int)resultado.Value == 1;
        }

        public async Task<FechasPermitidasCompra> LeerFechasCompra(string empresa, string usuario, NVEntities db)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            var filas = await db.Database.SqlQuery<FechasPermitidasCompra>(
                "SELECT FechaInicialCmp, FechaFinalCmp FROM FechasPermitidas WHERE Empresa = @Empresa AND Usuario = @Usuario",
                new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa },
                new SqlParameter("@Usuario", SqlDbType.NVarChar, 128) { Value = usuario.Trim() }
            ).ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }
    }

    /// <summary>NestoAPI#668: resultado de comprobar la fecha de una factura de compra.</summary>
    public class ComprobacionFechaCompra
    {
        public bool EsValida { get; set; }
        /// <summary>Primera fecha de compras permitida para el usuario, si la hay y es posterior.</summary>
        public DateTime? FechaPropuesta { get; set; }
    }

    /// <summary>
    /// NestoAPI#668 (caso real 19/10/26, IVA del 3T presentado): la conciliación crea la factura de
    /// compra con la fecha del movimiento del banco y prdCrearFacturaCmp da «Fecha no permitida».
    /// Antes de crear nada se comprueba la fecha y, si las compras están cerradas, se propone la
    /// FechaInicialCmp del usuario (si es posterior y está dentro de FechaFinalCmp). La propuesta se
    /// vuelve a pasar por prdFechaEsVálida: solo se ofrece una fecha que el SP va a aceptar.
    /// </summary>
    internal class ComprobadorFechaFacturaCompra
    {
        private readonly IFechasPermitidasCompras _fechasPermitidas;

        public ComprobadorFechaFacturaCompra(IFechasPermitidasCompras fechasPermitidas)
        {
            _fechasPermitidas = fechasPermitidas;
        }

        public async Task<ComprobacionFechaCompra> Comprobar(string empresa, DateTime fecha, string usuario, NVEntities db)
        {
            try
            {
                if (await _fechasPermitidas.EsFechaCompraValida(empresa, fecha, usuario, db).ConfigureAwait(false))
                {
                    return new ComprobacionFechaCompra { EsValida = true };
                }

                FechasPermitidasCompra fechas = await _fechasPermitidas.LeerFechasCompra(empresa, usuario, db).ConfigureAwait(false);
                DateTime? propuesta = CalcularFechaPropuesta(fecha, fechas?.FechaInicialCmp, fechas?.FechaFinalCmp);
                if (propuesta.HasValue && !await _fechasPermitidas.EsFechaCompraValida(empresa, propuesta.Value, usuario, db).ConfigureAwait(false))
                {
                    propuesta = null;
                }
                return new ComprobacionFechaCompra { EsValida = false, FechaPropuesta = propuesta };
            }
            catch (Exception ex)
            {
                // La comprobación es una ayuda: si falla (permisos, SP…), se sigue como antes y el
                // SP de la factura decide (con su «Fecha no permitida» de siempre).
                try
                {
                    ErrorLog.GetDefault(null)?.Log(new Error(new Exception(
                        $"NestoAPI#668: no se ha podido comprobar la fecha de compras {fecha:dd/MM/yyyy} del usuario {usuario}", ex)));
                }
                catch
                {
                    // el registro nunca debe romper la contabilización
                }
                return new ComprobacionFechaCompra { EsValida = true };
            }
        }

        /// <summary>
        /// La FechaInicialCmp cuando es posterior a la fecha pedida y no pasa de FechaFinalCmp
        /// (sin FechaFinalCmp, abierta). En cualquier otro caso, no hay propuesta.
        /// </summary>
        internal static DateTime? CalcularFechaPropuesta(DateTime fecha, DateTime? fechaInicialCmp, DateTime? fechaFinalCmp)
        {
            if (!fechaInicialCmp.HasValue)
            {
                return null;
            }
            DateTime inicial = fechaInicialCmp.Value.Date;
            if (inicial <= fecha.Date)
            {
                return null;
            }
            if (fechaFinalCmp.HasValue && inicial > fechaFinalCmp.Value.Date)
            {
                return null;
            }
            return inicial;
        }
    }

    /// <summary>
    /// NestoAPI#668: fechas con las que se crea cada pieza de la factura de compra. Con FechaFactura,
    /// el pedido, el albarán y la factura van con ella; el pago se queda SIEMPRE con la fecha del
    /// movimiento (la del pedido que manda el cliente) para que la conciliación siga cuadrando día a día.
    /// Sin FechaFactura, todo como antes (el albarán con la fecha de hoy, como siempre).
    /// </summary>
    internal class FechasFacturaCompra
    {
        public DateTime FechaPedido { get; private set; }
        /// <summary>Null = la de siempre (ahora).</summary>
        public DateTime? FechaAlbaran { get; private set; }
        public DateTime FechaFactura { get; private set; }
        public DateTime FechaPago { get; private set; }

        public static FechasFacturaCompra Calcular(DateTime fechaMovimiento, DateTime? fechaFactura)
        {
            DateTime fechaPedido = fechaFactura?.Date ?? fechaMovimiento;
            return new FechasFacturaCompra
            {
                FechaPedido = fechaPedido,
                FechaAlbaran = fechaFactura?.Date,
                FechaFactura = fechaPedido,
                FechaPago = fechaMovimiento
            };
        }

        /// <summary>
        /// Pone la fecha de la factura en el pedido y sus líneas. Idempotente (el reintento por
        /// deadlock vuelve a pasar por aquí). El primer vencimiento no queda antes de la factura.
        /// </summary>
        public void AplicarAlPedido(PedidoCompraDTO pedido)
        {
            if (!FechaAlbaran.HasValue || pedido == null)
            {
                return;
            }
            pedido.Fecha = FechaPedido;
            if (pedido.PrimerVencimiento < FechaPedido)
            {
                pedido.PrimerVencimiento = FechaPedido;
            }
            foreach (LineaPedidoCompraDTO linea in pedido.Lineas)
            {
                linea.FechaRecepcion = FechaPedido;
            }
        }
    }

    /// <summary>NestoAPI#668: las compras están cerradas en la fecha pedida y hay una fecha que proponer.</summary>
    internal class FechaCompraNoPermitidaException : Exception
    {
        public const string CODIGO = "FechaCompraNoPermitida";

        public FechaCompraNoPermitidaException(DateTime fecha, DateTime fechaPropuesta)
            : base(Mensaje(fecha, fechaPropuesta))
        {
            Fecha = fecha;
            FechaPropuesta = fechaPropuesta;
        }

        public DateTime Fecha { get; }
        public DateTime FechaPropuesta { get; }

        internal static string Mensaje(DateTime fecha, DateTime fechaPropuesta)
        {
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            return $"Las compras están cerradas hasta el {fechaPropuesta.AddDays(-1).ToString("dd/MM/yyyy", es)}: " +
                $"no se puede contabilizar la factura con fecha {fecha.ToString("dd/MM/yyyy", es)}. " +
                $"La primera fecha permitida es el {fechaPropuesta.ToString("dd/MM/yyyy", es)}.";
        }

        /// <summary>Cuerpo del 409 que devuelve CrearAlbaranYFactura.</summary>
        public FechaCompraNoPermitidaDTO ToDTO()
        {
            return new FechaCompraNoPermitidaDTO
            {
                Codigo = CODIGO,
                Fecha = Fecha,
                FechaPropuesta = FechaPropuesta,
                Message = Message
            };
        }
    }

    /// <summary>NestoAPI#668: cuerpo del 409 de api/PedidosCompra/CrearAlbaranYFactura.</summary>
    public class FechaCompraNoPermitidaDTO
    {
        public string Codigo { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime FechaPropuesta { get; set; }
        /// <summary>Texto para quien no conozca el código (p. ej. las facturas de Amazon).</summary>
        public string Message { get; set; }
    }
}
