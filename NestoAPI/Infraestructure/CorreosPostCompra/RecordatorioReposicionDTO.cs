using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    // NestoAPI#532: datos del recordatorio de reposición (ventas de Nesto, todos los canales).

    /// <summary>Un día en que el cliente compró (albarán) un producto, con lo que se llevó ese día.</summary>
    public class CompraDiaReposicion
    {
        public string Cliente { get; set; }
        public string Producto { get; set; }
        public DateTime Dia { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>
    /// Línea que todavía no es albarán: pedido pendiente o en curso (Estado -1..1) o nota de entrega (-2).
    /// </summary>
    public class LineaPendienteReposicion
    {
        public string Cliente { get; set; }
        public string Producto { get; set; }
        public short Estado { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>Lo que hace falta de la ficha del producto para decidir si se puede recordar.</summary>
    public class ProductoReposicion
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        /// <summary>La marca.</summary>
        public string Familia { get; set; }
        public short Estado { get; set; }
        public bool Ficticio { get; set; }
    }

    /// <summary>Lo que hace falta de la ficha del cliente para escribirle.</summary>
    public class ClienteReposicion
    {
        public string Cliente { get; set; }
        public string Nombre { get; set; }
        public string Email { get; set; }
        public short Estado { get; set; }
        public string Vendedor { get; set; }
        public string VendedorNombre { get; set; }
    }

    /// <summary>
    /// Un par cliente-producto al que, por su propio ritmo de compra, ya le tocaría reponer.
    /// <see cref="Motivo"/> null = se avisaría; si no, por qué se queda fuera.
    /// </summary>
    public class CandidatoReposicionDTO
    {
        public string Cliente { get; set; }
        public string Producto { get; set; }
        public string NombreProducto { get; set; }
        public int NumeroCompras { get; set; }
        public DateTime PrimeraCompra { get; set; }
        public DateTime UltimaCompra { get; set; }
        /// <summary>Mediana de días entre compras (con el mínimo aplicado).</summary>
        public int IntervaloDias { get; set; }
        public int DiasDesdeUltimaCompra { get; set; }
        /// <summary>Base imponible media de cada compra: ordena los productos dentro del correo.</summary>
        public decimal ImporteHabitual { get; set; }
        public string Motivo { get; set; }
        public string EnlaceTienda { get; set; }
        public bool SeAvisaria => Motivo == null;
    }

    /// <summary>El correo de reposición de un cliente: hasta 3 productos.</summary>
    public class RecordatorioReposicionClienteDTO
    {
        public string Cliente { get; set; }
        public string Nombre { get; set; }
        public string Email { get; set; }
        /// <summary>Nombre del comercial (vacío si es el vendedor general): «si prefieres, pídeselo a...».</summary>
        public string VendedorNombre { get; set; }
        /// <summary>Grupo de control: sería elegible, pero NO se le escribe (para medir el efecto del correo).</summary>
        public bool GrupoControl { get; set; }
        public List<CandidatoReposicionDTO> Productos { get; set; } = new List<CandidatoReposicionDTO>();
    }

    /// <summary>Resultado del cálculo en seco: a quién se escribiría esta semana y quién se queda fuera.</summary>
    public class ResultadoRecordatorioReposicionDTO
    {
        public DateTime Fecha { get; set; }
        public DateTime HistorialDesde { get; set; }
        public string Consumibles { get; set; }
        public List<RecordatorioReposicionClienteDTO> SeAvisarian { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        public List<RecordatorioReposicionClienteDTO> GrupoControl { get; set; } = new List<RecordatorioReposicionClienteDTO>();
        /// <summary>Pares que tocarían por ritmo pero se quedan fuera, con el motivo.</summary>
        public List<CandidatoReposicionDTO> Descartes { get; set; } = new List<CandidatoReposicionDTO>();
        public int Correos => SeAvisarian.Count;
        public int Productos => SeAvisarian.Sum(c => c.Productos.Count);
    }
}
