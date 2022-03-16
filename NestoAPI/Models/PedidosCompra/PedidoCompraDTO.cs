using Nesto.Modulos.PedidoCompra.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.PedidosCompra
{
    public class PedidoCompraDTO
    {
        public PedidoCompraDTO()
        {
            Lineas = new List<LineaPedidoCompraDTO>();
            ParametrosIva = new List<ParametrosIvaCompra>();
        }
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Proveedor { get; set; }
        public string Contacto { get; set; }
        public DateTime Fecha { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public DateTime PrimerVencimiento { get; set; }
        public int DiasEnServir { get; set; }
        public string CodigoIvaProveedor { get; set; }
        public string FacturaProveedor { get; set; }
        public string Comentarios { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string PeriodoFacturacion { get; set; }
        public string PathPedido { get; set; }
        public string CorreoRecepcionPedidos { get; set; }
        public decimal BaseImponible { get => Math.Round(Lineas.Sum(l => l.BaseImponible), 2, MidpointRounding.AwayFromZero); }
        public decimal Total { get => Math.Round(Lineas.Sum(l => l.Total), 2, MidpointRounding.AwayFromZero); }
        public string Usuario { get; set; }
        public IEnumerable<LineaPedidoCompraDTO> Lineas { get; set; }
        public IEnumerable<ParametrosIvaCompra> ParametrosIva { get; set; }


        internal CabPedidoCmp ToCabPedidoCmp()
        {
            CabPedidoCmp cabecera = new CabPedidoCmp
            {
                Empresa = Empresa,
                Número = Id,
                NºProveedor = Proveedor,
                Contacto = Contacto,
                Fecha = Fecha,
                FormaPago = FormaPago,
                PlazosPago = PlazosPago,
                PrimerVencimiento = PrimerVencimiento,
                IVA = CodigoIvaProveedor,
                PeriodoFacturación = PeriodoFacturacion,
                DíasEnServir = (byte)DiasEnServir,
                Usuario = Usuario,
                Fecha_Modificación = DateTime.Now
            };

            foreach (var linea in Lineas.Where(l => !(l.Cantidad == 0 && l.TipoLinea == Constantes.TiposLineaCompra.PRODUCTO)))
            {
                cabecera.LinPedidoCmps.Add(new LinPedidoCmp
                {
                    Empresa = cabecera.Empresa,
                    Número = cabecera.Número,
                    NºProveedor = Proveedor,
                    Contacto = Contacto,
                    Almacén = Constantes.Almacenes.ALGETE,
                    Delegación = Constantes.Empresas.DELEGACION_POR_DEFECTO,
                    FormaVenta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                    Estado = (short)linea.Estado,
                    TipoLínea = linea.TipoLinea,
                    Producto = linea.Producto,
                    Texto = linea.Texto,
                    FechaRecepción = new DateTime(linea.FechaRecepcion.Year, linea.FechaRecepcion.Month, linea.FechaRecepcion.Day),
                    Cantidad = (short)(linea.CantidadCobrada != null ? linea.CantidadCobrada : linea.Cantidad),
                    Precio = linea.PrecioUnitario,
                    Descuento = linea.DescuentoLinea,
                    DescuentoProveedor = linea.DescuentoProveedor,
                    DescuentoProducto = linea.DescuentoProducto,
                    AplicarDto = linea.AplicarDescuentos,
                    IVA = linea.CodigoIvaProducto,
                    Grupo = linea.Grupo,
                    Subgrupo = linea.Subgrupo,
                    PrecioTarifa = linea.PrecioTarifa,
                    EstadoProducto = (short)linea.EstadoProducto,
                    VistoBueno = true,
                    Bruto = linea.Bruto,
                    ImporteDto = linea.ImporteDescuento,
                    BaseImponible = linea.BaseImponible,
                    ImporteIVA = linea.ImporteIva,
                    Total = linea.Total,
                    Usuario = cabecera.Usuario,
                    Fecha_Modificación = DateTime.Now
                });
                if (linea.CantidadRegalo != null && linea.CantidadRegalo != 0)
                {
                    cabecera.LinPedidoCmps.Add(new LinPedidoCmp
                    {
                        Empresa = cabecera.Empresa,
                        Número = cabecera.Número,
                        NºProveedor = Proveedor,
                        Contacto = Contacto,
                        Almacén = Constantes.Almacenes.ALGETE,
                        Delegación = Constantes.Empresas.DELEGACION_POR_DEFECTO,
                        FormaVenta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                        Estado = (short)linea.Estado,
                        TipoLínea = linea.TipoLinea,
                        Producto = linea.Producto,
                        Texto = linea.Texto,
                        FechaRecepción = new DateTime(linea.FechaRecepcion.Year, linea.FechaRecepcion.Month, linea.FechaRecepcion.Day),
                        Cantidad = (short)linea.CantidadRegalo,
                        Precio = 0,
                        Descuento = linea.DescuentoLinea,
                        DescuentoProveedor = linea.DescuentoProveedor,
                        DescuentoProducto = linea.DescuentoProducto,
                        AplicarDto = linea.AplicarDescuentos,
                        IVA = linea.CodigoIvaProducto,
                        Grupo = linea.Grupo,
                        Subgrupo = linea.Subgrupo,
                        PrecioTarifa = linea.PrecioTarifa,
                        EstadoProducto = (short)linea.EstadoProducto,
                        VistoBueno = true,
                        Bruto = 0,
                        ImporteDto = 0,
                        BaseImponible = 0,
                        ImporteIVA = 0,
                        Total = 0,
                        Usuario = cabecera.Usuario,
                        Fecha_Modificación = DateTime.Now
                    });
                }
            }

            return cabecera;
        }
    }
}