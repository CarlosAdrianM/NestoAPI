using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Facturae
{
    /// <summary>
    /// NestoAPI#903: los datos de UNA factura emitida tal como van al fichero Facturae 3.2.2 (sin firmar) que se importa en
    /// B2Brouter para FACe. Lo rellena <see cref="ConstructorModeloFacturae"/> (puro) y lo escribe <see cref="GeneradorFacturae"/>.
    /// Los importes son los de la factura real; los totales del fichero se calculan de ellos en el generador.
    /// </summary>
    public class ModeloFacturae
    {
        public ParteFacturae Emisor { get; set; }
        public ParteFacturae Receptor { get; set; }
        /// <summary>Los centros DIR3 del organismo (BuyerParty/AdministrativeCentres), en orden de rol.</summary>
        public List<CentroAdministrativoFacturae> Centros { get; set; } = new List<CentroAdministrativoFacturae>();
        /// <summary>Número SIN la serie (como en Verifactu: NV2616075 → 2616075).</summary>
        public string Numero { get; set; }
        public string Serie { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>Null si no es rectificativa (InvoiceClass OO); con ella, OR y el bloque Corrective.</summary>
        public RectificativaFacturae Rectificativa { get; set; }
        public List<LineaFacturae> Lineas { get; set; } = new List<LineaFacturae>();
        /// <summary>El desglose de impuestos de la cabecera (uno por tipo de IVA y recargo), con las cuotas de la factura real.</summary>
        public List<ImpuestoFacturae> Impuestos { get; set; } = new List<ImpuestoFacturae>();
        public List<VencimientoFacturae> Vencimientos { get; set; } = new List<VencimientoFacturae>();

        /// <summary>BatchIdentifier: NIF del emisor + número + serie (máx. 70).</summary>
        public string IdentificadorLote => Recortar((Emisor?.Nif ?? string.Empty) + (Numero ?? string.Empty) + (Serie ?? string.Empty), 70);

        internal static string Recortar(string texto, int maximo)
            => texto == null ? null : (texto.Length <= maximo ? texto : texto.Substring(0, maximo));
    }

    /// <summary>NestoAPI#903: el emisor o el receptor (TaxIdentification + LegalEntity con AddressInSpain).</summary>
    public class ParteFacturae
    {
        /// <summary>NIF sin «ES» delante ni separadores.</summary>
        public string Nif { get; set; }
        /// <summary>«J» (persona jurídica) o «F» (física).</summary>
        public string TipoPersona { get; set; } = "J";
        public string Nombre { get; set; }
        public DireccionFacturae Direccion { get; set; }
    }

    /// <summary>NestoAPI#903: AddressInSpain (Address 80, PostCode 5 cifras, Town 50, Province 20, CountryCode ESP).</summary>
    public class DireccionFacturae
    {
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Pais { get; set; } = "ESP";
    }

    /// <summary>NestoAPI#903: un centro DIR3 del organismo (AdministrativeCentre).</summary>
    public class CentroAdministrativoFacturae
    {
        /// <summary>CentreCode: el código DIR3.</summary>
        public string Codigo { get; set; }
        /// <summary>RoleTypeCode: 01 oficina contable, 02 órgano gestor, 03 unidad tramitadora, 04 órgano proponente.</summary>
        public string Rol { get; set; }
        /// <summary>Name (máx. 40), opcional.</summary>
        public string Nombre { get; set; }
        public DireccionFacturae Direccion { get; set; }
        /// <summary>CentreDescription («Oficina contable»…).</summary>
        public string Descripcion { get; set; }
    }

    /// <summary>NestoAPI#903: una InvoiceLine. Importes con todos sus decimales (el generador escribe hasta 8).</summary>
    public class LineaFacturae
    {
        public string ReferenciaPedido { get; set; }
        public string Contrato { get; set; }
        public string Expediente { get; set; }
        /// <summary>Albarán de la línea (DeliveryNotesReferences), o null.</summary>
        public string Albaran { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        public string Descripcion { get; set; }
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        /// <summary>TotalCost = Cantidad × PrecioUnitario (el Bruto de la línea).</summary>
        public decimal CosteTotal { get; set; }
        /// <summary>DiscountRate en % (null = sin descuento).</summary>
        public decimal? PorcentajeDescuento { get; set; }
        /// <summary>El descuento de la factura: ROUND(Bruto, 2) − base imponible (= ImporteDto).</summary>
        public decimal ImporteDescuento { get; set; }
        public string MotivoDescuento { get; set; }
        /// <summary>
        /// ROUND(Bruto, 2) − Bruto: el redondeo a céntimos que hace la factura antes de restar el descuento. Va como Charge si
        /// agranda el importe y como Discount si lo achica, para que GrossAmount = TotalCost − descuentos + cargos = la base.
        /// </summary>
        public decimal ImporteRedondeo { get; set; }
        /// <summary>GrossAmount = la base imponible de la línea en la factura.</summary>
        public decimal ImporteBruto { get; set; }
        public decimal TipoIva { get; set; }
        /// <summary>Cuota de IVA de la línea, a 2 decimales; la de la línea mayor de cada tipo lleva el ajuste del céntimo.</summary>
        public decimal CuotaIva { get; set; }
        /// <summary>Recargo de equivalencia en % (0 = no lleva).</summary>
        public decimal TipoRecargo { get; set; }
        public decimal CuotaRecargo { get; set; }
        public string CodigoArticulo { get; set; }
    }

    /// <summary>NestoAPI#903: un Tax de TaxesOutputs de la cabecera (IVA, TaxTypeCode 01, con su recargo si lo lleva).</summary>
    public class ImpuestoFacturae
    {
        public decimal TipoIva { get; set; }
        public decimal Base { get; set; }
        public decimal Cuota { get; set; }
        public decimal TipoRecargo { get; set; }
        public decimal CuotaRecargo { get; set; }
    }

    /// <summary>NestoAPI#903: un Installment de PaymentDetails.</summary>
    public class VencimientoFacturae
    {
        public DateTime Fecha { get; set; }
        public decimal Importe { get; set; }
        /// <summary>PaymentMeans de Facturae (01 contado, 02 recibo domiciliado, 04 transferencia, 19 tarjeta…).</summary>
        public string MedioPago { get; set; }
        /// <summary>AccountToBeCredited/IBAN (solo con transferencia), sin espacios.</summary>
        public string Iban { get; set; }
        /// <summary>La forma de pago de Nesto (TRN, RCB…), solo para la comprobación.</summary>
        public string FormaPagoNesto { get; set; }
    }

    /// <summary>NestoAPI#903: el bloque Corrective de una rectificativa (la factura que rectifica y por qué).</summary>
    public class RectificativaFacturae
    {
        public string NumeroOriginal { get; set; }
        public string SerieOriginal { get; set; }
        public DateTime FechaOriginal { get; set; }
        /// <summary>ReasonCode (16 = base imponible).</summary>
        public string CodigoMotivo { get; set; } = "16";
        public string DescripcionMotivo { get; set; } = "Base imponible";
        /// <summary>CorrectionMethod (02 = por diferencias: nuestras rectificativas son abonos en negativo, la «I» de Verifactu).</summary>
        public string CodigoMetodo { get; set; } = "02";
        public string DescripcionMetodo { get; set; } = "Rectificación por diferencias";
        public string MotivoAdicional { get; set; }
    }
}
