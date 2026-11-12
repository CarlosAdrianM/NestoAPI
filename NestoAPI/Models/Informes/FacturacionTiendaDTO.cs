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
        /// <summary>Sugerencia 648: el contacto de ENTREGA del (primer) pedido de la factura; nombre y dirección son los suyos.</summary>
        public string Contacto { get; set; }
        /// <summary>Sugerencia 648: el primer pedido de la factura con líneas de la tienda (el que da la dirección de entrega).</summary>
        public int? Pedido { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        /// <summary>El comercial: el vendedor de la cabecera de la factura (CabFacturaVta.Vendedor), no el de la ficha del cliente.</summary>
        public string Vendedor { get; set; }
        public string NombreVendedor { get; set; }
        /// <summary>Sugerencia 648: la forma de venta de sus líneas de la tienda (TIE, DIR…; varias, separadas por comas).</summary>
        public string FormaVenta { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>NestoAPI#658: lo facturado en la tienda a un cliente (contacto) en el periodo con un comercial (vendedor de la factura):
    /// un cliente con facturas de dos comerciales sale en dos filas.</summary>
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
        public int NumeroFacturas { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>
    /// NestoAPI#719 (sugerencia 792 de Paloma, gemela de Nesto#559): lo facturado en una tienda de enero a <see cref="Hasta"/>, mes a
    /// mes (tipo «Acumulado») y, en el «Comparativo», el mismo periodo (1 de enero al mismo día y mes) de los <see cref="AniosAnteriores"/>
    /// años anteriores, con la variación del año de <see cref="Hasta"/> respecto a cada uno. Base imponible (sin IVA), con las mismas
    /// reglas que el informe de la semana (líneas del almacén de la tienda, empresa normal y espejo, rectificativas restan, cursos incluidos).
    /// </summary>
    public class FacturacionTiendaAnualDTO
    {
        /// <summary>«Acumulado» o «Comparativo».</summary>
        public string Tipo { get; set; }
        public string Tienda { get; set; }
        /// <summary>El año del informe (el de <see cref="Hasta"/>).</summary>
        public int Anio { get; set; }
        /// <summary>Siempre el 1 de enero de <see cref="Anio"/>.</summary>
        public DateTime Desde { get; set; }
        /// <summary>Último día incluido.</summary>
        public DateTime Hasta { get; set; }
        /// <summary>Los años anteriores comparados, del más reciente al más antiguo (vacío en el «Acumulado»).</summary>
        public List<int> AniosAnteriores { get; set; } = new List<int>();
        /// <summary>De enero al mes de <see cref="Hasta"/> (el último, hasta ese día), todos aunque no tengan facturas.</summary>
        public List<MesFacturacionTiendaDTO> Meses { get; set; } = new List<MesFacturacionTiendaDTO>();
        /// <summary>La suma de los meses (Mes = 0, NombreMes «Total»; Acumulado = BaseImponible).</summary>
        public MesFacturacionTiendaDTO Total { get; set; }
    }

    /// <summary>NestoAPI#719: un mes del informe anual de la tienda.</summary>
    public class MesFacturacionTiendaDTO
    {
        /// <summary>1 a 12 (0 en el total).</summary>
        public int Mes { get; set; }
        public string NombreMes { get; set; }
        /// <summary>Del año del informe, primer y último día del mes que entran (el último mes acaba en Hasta).</summary>
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public int NumeroFacturas { get; set; }
        public decimal BaseImponible { get; set; }
        /// <summary>De la base, lo de la serie de cursos (CV).</summary>
        public decimal BaseImponibleCursos { get; set; }
        /// <summary>La base de enero hasta este mes, incluido.</summary>
        public decimal Acumulado { get; set; }
        /// <summary>Solo en el «Comparativo»: el mismo mes de cada año anterior, en el orden de AniosAnteriores.</summary>
        public List<ComparativoMesFacturacionTiendaDTO> Anteriores { get; set; } = new List<ComparativoMesFacturacionTiendaDTO>();
    }

    /// <summary>NestoAPI#719: el mismo mes (y días) de un año anterior y la variación del año del informe respecto a él.</summary>
    public class ComparativoMesFacturacionTiendaDTO
    {
        public int Anio { get; set; }
        public int NumeroFacturas { get; set; }
        public decimal BaseImponible { get; set; }
        /// <summary>Base del año del informe menos la de este año.</summary>
        public decimal VariacionImporte { get; set; }
        /// <summary>VariacionImporte sobre la base de este año, en % con dos decimales (12,5 = 12,5 %); null si esa base es 0.</summary>
        public decimal? VariacionPorcentaje { get; set; }
    }

    /// <summary>NestoAPI#719: una fila de la consulta agregada (año, mes) del informe anual.</summary>
    public class FilaMesFacturacionTiendaDTO
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public int NumeroFacturas { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal BaseImponibleCursos { get; set; }
    }

    /// <summary>
    /// Sugerencia 648: cuerpo de POST api/Informes/FacturacionTienda/Correo. Tienda y fechas como en el GET (null: la suya y la
    /// semana anterior). <see cref="Correo"/> null o vacío: el CorreoDefecto del usuario.
    /// </summary>
    public class PeticionCorreoFacturacionTiendaDTO
    {
        public string Tienda { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string Correo { get; set; }
    }

    /// <summary>Sugerencia 648: a quién se ha mandado el Excel (y lo que llevaba, para enseñarlo).</summary>
    public class CorreoFacturacionTiendaDTO
    {
        public string Correo { get; set; }
        public string Tienda { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public int NumeroFacturas { get; set; }
        public decimal TotalBaseImponible { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>Sugerencia 648: el correo al que se manda el Excel por defecto (GET api/Informes/FacturacionTienda/Correo).</summary>
    public class CorreoUsuarioFacturacionTiendaDTO
    {
        /// <summary>El CorreoDefecto del usuario; null si no tiene.</summary>
        public string Correo { get; set; }
    }
}
