using System;

namespace NestoAPI.Models
{
    /// <summary>
    /// Lo que devuelve GET api/Empresas: las columnas de la tabla y NADA de las navegaciones de EF.
    ///
    /// NestoAPI#472: el endpoint devolvía la entidad <see cref="Empresa"/> entera, con sus ~60
    /// navegaciones serializadas como arrays (vacíos, porque el lazy loading va apagado). Nesto
    /// deserializaba esa respuesta en su propio modelo de EF, donde <c>Vendedores</c> es un
    /// objeto y no una colección (los dos EDMX bautizaron al revés la misma navegación), y
    /// reventaba SIEMPRE: un <c>[]</c> tampoco cabe en una propiedad escalar. Consecuencia:
    /// Agencias se quedaba sin lista de empresas y la etiqueta de ASM/CEX dejaba de salir, en
    /// silencio.
    ///
    /// Se mantienen TODAS las columnas escalares, con el relleno de los char tal cual (Número
    /// "1  "), para que la respuesta sea idéntica a la de antes menos las navegaciones: los
    /// cuatro llamantes de Nesto leen campos sueltos (Número, Nombre, FormaPagoEfectivo,
    /// FormaVentaVarios, DelegaciónVarios) y ninguno debe notar el cambio. Adelgazar esta lista
    /// a lo que de verdad se usa es trabajo de Nesto#340, cuando Agencias deje de deserializar
    /// contra su entidad de EF.
    /// </summary>
    public class EmpresaDTO
    {
        public string Número { get; set; }
        public string Nombre { get; set; }
        public string NIF { get; set; }
        public string Sufijo { get; set; }
        public bool MostrarMenú { get; set; }
        public string IVA_por_defecto { get; set; }
        public string CtaDtoCliente { get; set; }
        public string CtaDescuentoVta { get; set; }
        public string CtaDtoPPVta { get; set; }
        public string CtaDtoProveedor { get; set; }
        public string CtaDescuentoCmp { get; set; }
        public string CtaDtoPPCmp { get; set; }
        public string DelegaciónVarios { get; set; }
        public string FormaVentaVarios { get; set; }
        public string CtaCaja { get; set; }
        public string CtaDescuadre { get; set; }
        public Nullable<byte> LongitudCuenta { get; set; }
        public string FormaPagoTalón { get; set; }
        public string FormaPagoEfectivo { get; set; }
        public string FormaPagoTarjeta { get; set; }
        public string FormaPagoPagaré { get; set; }
        public string PlazosPagoDefecto { get; set; }
        public string VendedorVarios { get; set; }
        public string Dirección { get; set; }
        public string Dirección2 { get; set; }
        public string Teléfono { get; set; }
        public string Fax { get; set; }
        public string CodPostal { get; set; }
        public string Población { get; set; }
        public string Provincia { get; set; }
        public string Texto { get; set; }
        public string Logotipo { get; set; }
        public string TipoIvaDefecto { get; set; }
        public bool GeneraBonificaciónDefecto { get; set; }
        public byte VigenciaBonificación { get; set; }
        public string CtaRegularización { get; set; }
        public string CtaRemanente { get; set; }
        public string CtaDiferenciasNegativasEjerciciosAnteriores { get; set; }
        public string CtaAPagarIVA { get; set; }
        public string CtaACompensarIVA { get; set; }
        public string TipoIvaDefectoSoportado { get; set; }
        public string ProductoRegularizaciones { get; set; }
        public string ProductoReparacion { get; set; }
        public string Email { get; set; }
        public string Web { get; set; }
        public string EmpresaDatafono { get; set; }
        public bool EmpresaCurso { get; set; }
        public string EstadoReembolsado { get; set; }
        public string ProveedorCostes { get; set; }
        public string ctaDiferenciasEnCompras { get; set; }
        public int UltAsientoContable { get; set; }
        public string ProveedorIRPF { get; set; }
        public string ContactoProveedorIRPF { get; set; }
        public decimal PorcentajeFacturadoNoEntregadoParaPedidoCmpAuto { get; set; }
        public string TextoFactura { get; set; }
        public int StockAntiguo { get; set; }
        public Nullable<int> EstadoProductoRegalo { get; set; }
        public string InformeAlbaran { get; set; }
        public string InformeFactura { get; set; }
        public string InformePedidoCmp { get; set; }
        public string InformeCartaPagare { get; set; }
        public string InformeCartaPagareManual { get; set; }
        public string ImpresoraPagare { get; set; }
        public bool OcultarB { get; set; }
        public bool MostrarOperador { get; set; }
        public Nullable<int> MaxPickingListado { get; set; }
        public Nullable<int> MaxPickingPorListar { get; set; }
        public string InformeFacturaConMembrete { get; set; }
        public bool VerFormaVentaCng { get; set; }
        public Nullable<decimal> PreciosRenting36 { get; set; }
        public Nullable<decimal> PreciosRenting48 { get; set; }
        public Nullable<decimal> PreciosRentingImporte { get; set; }
        public Nullable<decimal> PorcentajeVR36 { get; set; }
        public Nullable<decimal> PorcentajeVR48 { get; set; }
        public Nullable<decimal> PorcentajeRentingProductos { get; set; }
        public Nullable<decimal> PorcentajeRentingServicios { get; set; }
        public bool EnviarCorreoProductoNuevo { get; set; }
        public string ctaTrabajosRealizados { get; set; }
        public Nullable<System.DateTime> FechaPicking { get; set; }
        public bool BloquearTraspasoRuta { get; set; }
        public string RutaImpagado { get; set; }
        public string Usuario { get; set; }
        public System.DateTime Fecha_Modificación { get; set; }

        public static EmpresaDTO DesdeEntidad(Empresa e)
        {
            if (e == null)
            {
                return null;
            }
            return new EmpresaDTO
            {
                Número = e.Número,
                Nombre = e.Nombre,
                NIF = e.NIF,
                Sufijo = e.Sufijo,
                MostrarMenú = e.MostrarMenú,
                IVA_por_defecto = e.IVA_por_defecto,
                CtaDtoCliente = e.CtaDtoCliente,
                CtaDescuentoVta = e.CtaDescuentoVta,
                CtaDtoPPVta = e.CtaDtoPPVta,
                CtaDtoProveedor = e.CtaDtoProveedor,
                CtaDescuentoCmp = e.CtaDescuentoCmp,
                CtaDtoPPCmp = e.CtaDtoPPCmp,
                DelegaciónVarios = e.DelegaciónVarios,
                FormaVentaVarios = e.FormaVentaVarios,
                CtaCaja = e.CtaCaja,
                CtaDescuadre = e.CtaDescuadre,
                LongitudCuenta = e.LongitudCuenta,
                FormaPagoTalón = e.FormaPagoTalón,
                FormaPagoEfectivo = e.FormaPagoEfectivo,
                FormaPagoTarjeta = e.FormaPagoTarjeta,
                FormaPagoPagaré = e.FormaPagoPagaré,
                PlazosPagoDefecto = e.PlazosPagoDefecto,
                VendedorVarios = e.VendedorVarios,
                Dirección = e.Dirección,
                Dirección2 = e.Dirección2,
                Teléfono = e.Teléfono,
                Fax = e.Fax,
                CodPostal = e.CodPostal,
                Población = e.Población,
                Provincia = e.Provincia,
                Texto = e.Texto,
                Logotipo = e.Logotipo,
                TipoIvaDefecto = e.TipoIvaDefecto,
                GeneraBonificaciónDefecto = e.GeneraBonificaciónDefecto,
                VigenciaBonificación = e.VigenciaBonificación,
                CtaRegularización = e.CtaRegularización,
                CtaRemanente = e.CtaRemanente,
                CtaDiferenciasNegativasEjerciciosAnteriores = e.CtaDiferenciasNegativasEjerciciosAnteriores,
                CtaAPagarIVA = e.CtaAPagarIVA,
                CtaACompensarIVA = e.CtaACompensarIVA,
                TipoIvaDefectoSoportado = e.TipoIvaDefectoSoportado,
                ProductoRegularizaciones = e.ProductoRegularizaciones,
                ProductoReparacion = e.ProductoReparacion,
                Email = e.Email,
                Web = e.Web,
                EmpresaDatafono = e.EmpresaDatafono,
                EmpresaCurso = e.EmpresaCurso,
                EstadoReembolsado = e.EstadoReembolsado,
                ProveedorCostes = e.ProveedorCostes,
                ctaDiferenciasEnCompras = e.ctaDiferenciasEnCompras,
                UltAsientoContable = e.UltAsientoContable,
                ProveedorIRPF = e.ProveedorIRPF,
                ContactoProveedorIRPF = e.ContactoProveedorIRPF,
                PorcentajeFacturadoNoEntregadoParaPedidoCmpAuto = e.PorcentajeFacturadoNoEntregadoParaPedidoCmpAuto,
                TextoFactura = e.TextoFactura,
                StockAntiguo = e.StockAntiguo,
                EstadoProductoRegalo = e.EstadoProductoRegalo,
                InformeAlbaran = e.InformeAlbaran,
                InformeFactura = e.InformeFactura,
                InformePedidoCmp = e.InformePedidoCmp,
                InformeCartaPagare = e.InformeCartaPagare,
                InformeCartaPagareManual = e.InformeCartaPagareManual,
                ImpresoraPagare = e.ImpresoraPagare,
                OcultarB = e.OcultarB,
                MostrarOperador = e.MostrarOperador,
                MaxPickingListado = e.MaxPickingListado,
                MaxPickingPorListar = e.MaxPickingPorListar,
                InformeFacturaConMembrete = e.InformeFacturaConMembrete,
                VerFormaVentaCng = e.VerFormaVentaCng,
                PreciosRenting36 = e.PreciosRenting36,
                PreciosRenting48 = e.PreciosRenting48,
                PreciosRentingImporte = e.PreciosRentingImporte,
                PorcentajeVR36 = e.PorcentajeVR36,
                PorcentajeVR48 = e.PorcentajeVR48,
                PorcentajeRentingProductos = e.PorcentajeRentingProductos,
                PorcentajeRentingServicios = e.PorcentajeRentingServicios,
                EnviarCorreoProductoNuevo = e.EnviarCorreoProductoNuevo,
                ctaTrabajosRealizados = e.ctaTrabajosRealizados,
                FechaPicking = e.FechaPicking,
                BloquearTraspasoRuta = e.BloquearTraspasoRuta,
                RutaImpagado = e.RutaImpagado,
                Usuario = e.Usuario,
                Fecha_Modificación = e.Fecha_Modificación
            };
        }
    }
}
