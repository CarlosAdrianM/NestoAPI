using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Informes
{
    /// <summary>
    /// NestoAPI#658: lo facturado en una tienda (REI, ALC) en un periodo, cursos incluidos, para pasar las cifras de tienda.
    /// Va factura a factura (<see cref="Facturas"/>) y, además, agrupado por cliente (<see cref="Clientes"/>), con los totales al pie.
    /// Lo calcula <see cref="Infraestructure.Informes.GestorFacturacionTienda"/>.
    /// </summary>
    public class FacturacionTiendaDTO
    {
        public string Tienda { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public List<FacturaTiendaDTO> Facturas { get; set; } = new List<FacturaTiendaDTO>();
        public List<ClienteFacturacionTiendaDTO> Clientes { get; set; } = new List<ClienteFacturacionTiendaDTO>();
        public int NumeroFacturas { get; set; }
        public int NumeroClientes { get; set; }
        public decimal TotalBaseImponible { get; set; }
        public decimal Total { get; set; }
        /// <summary>De lo anterior, lo facturado en la serie de cursos (CV).</summary>
        public decimal TotalBaseImponibleCursos { get; set; }
    }

    /// <summary>NestoAPI#658: una factura con las líneas de la tienda (solo suman las líneas con el almacén de la tienda).</summary>
    public class FacturaTiendaDTO
    {
        public string Empresa { get; set; }
        public string Factura { get; set; }
        public DateTime Fecha { get; set; }
        public string Serie { get; set; }
        public bool EsCurso { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        /// <summary>Vendedor de la ficha del cliente (estética).</summary>
        public string Vendedor { get; set; }
        public string NombreVendedor { get; set; }
        /// <summary>Vendedor de peluquería del cliente (VendedoresClienteGrupoProducto, grupo PEL), si lo tiene.</summary>
        public string VendedorPeluqueria { get; set; }
        public string NombreVendedorPeluqueria { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>NestoAPI#658: lo facturado en la tienda a un cliente (contacto) en el periodo.</summary>
    public class ClienteFacturacionTiendaDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Vendedor { get; set; }
        public string NombreVendedor { get; set; }
        public string VendedorPeluqueria { get; set; }
        public string NombreVendedorPeluqueria { get; set; }
        public int NumeroFacturas { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }
    }
}
