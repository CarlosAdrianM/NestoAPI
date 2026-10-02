using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    // NestoAPI#559 / #553: recibir mercancía con un solo contrato para todos los tipos (compras de proveedor,
    // reposiciones entre almacenes y lo que venga). Lo usan Ariadna y Nesto (por ejemplo, las tiendas al
    // recibir sus reposiciones): nada de esto depende de la app.

    /// <summary>Algo que está por recibir en un almacén.</summary>
    public class RecepcionPendienteDTO
    {
        /// <summary>COMP (pedidos de compra de un proveedor) o REPO (reposición entre almacenes).</summary>
        public string Tipo { get; set; }
        /// <summary>COMP: el proveedor. REPO: el número de traspaso.</summary>
        public string Documento { get; set; }
        /// <summary>Lo que se enseña: el nombre del proveedor o «Reposición 80841».</summary>
        public string Titulo { get; set; }
        public DateTime? Fecha { get; set; }
        /// <summary>COMP: los pedidos de compra que tiene pendientes. REPO: vacío.</summary>
        public List<int> Pedidos { get; set; } = new List<int>();
        public int Lineas { get; set; }
        public int Unidades { get; set; }
    }

    /// <summary>Un producto que se espera recibir.</summary>
    public class LineaRecepcionDTO
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public bool SinCodigo { get; set; }
        public bool CodigoDuplicado { get; set; }
        /// <summary>Unidades que se esperan de ese producto (sumadas de todas sus líneas).</summary>
        public int Cantidad { get; set; }
        /// <summary>
        /// Lo que se dio por no servido hace poco (-99) y entra con su pedido si llega, en vez de contar como exceso
        /// (compras de proveedores sin control de pendientes).
        /// </summary>
        public List<RecuperableRecepcionDTO> Recuperables { get; set; } = new List<RecuperableRecepcionDTO>();
    }

    public class RecuperableRecepcionDTO
    {
        public int Pedido { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaNoServido { get; set; }
    }

    /// <summary>Lo que ha llegado de algo que se había dado por no servido y entra con su pedido.</summary>
    public class LineaRecuperadaDTO
    {
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaNoServido { get; set; }
        /// <summary>Para enseñarlo tal cual: «3 ud. de 45915 eran del pedido 220396, que se dio por no servido el 28/09…».</summary>
        public string Texto { get; set; }
    }

    public class RecepcionDTO
    {
        public string Tipo { get; set; }
        public string Documento { get; set; }
        public string Titulo { get; set; }
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        /// <summary>Si quien pregunta puede terminar esta recepción (depende del tipo).</summary>
        public bool PuedeTerminar { get; set; }
        /// <summary>Hay tipos que de momento solo se pueden leer y comparar (sin terminar).</summary>
        public bool SeTerminaDesdeAqui { get; set; }
        public List<LineaRecepcionDTO> Lineas { get; set; } = new List<LineaRecepcionDTO>();
    }

    public class ResultadoCasarRecepcionDTO
    {
        public string Tipo { get; set; }
        public string Documento { get; set; }
        /// <summary>Lo leído coincide con lo esperado: sin faltas, sobras ni productos que no tocan.</summary>
        public bool Cuadra { get; set; }
        /// <summary>Esperado frente a leído por producto. Ajeno = no se esperaba (no pedido / no enviado).</summary>
        public List<DiferenciaPreparacionDTO> Productos { get; set; } = new List<DiferenciaPreparacionDTO>();
        /// <summary>Lo que sobra y en realidad es lo que se dio por no servido hace poco: entrará con su pedido.</summary>
        public List<string> Recuperadas { get; set; } = new List<string>();
    }

    /// <summary>Lo que se manda para terminar una recepción. Vale igual desde Ariadna o desde Nesto.</summary>
    public class TerminarRecepcionDTO
    {
        /// <summary>
        /// Lo genera quien llama, una vez por recepción. Si la respuesta se pierde y se reenvía, no se recibe dos
        /// veces: se contesta que ya estaba terminada.
        /// </summary>
        public Guid IdRecepcion { get; set; }
        /// <summary>Lo contado de cada producto (leído o tecleado).</summary>
        public List<LecturaRecepcionDTO> Lecturas { get; set; } = new List<LecturaRecepcionDTO>();
        public string Dispositivo { get; set; }
    }

    public class DocumentoRecepcionDTO
    {
        /// <summary>COMP: el pedido de compra.</summary>
        public int Pedido { get; set; }
        /// <summary>COMP: el albarán de compra creado.</summary>
        public int Albaran { get; set; }
    }

    public class ResultadoTerminarRecepcionDTO
    {
        public string Tipo { get; set; }
        public string Documento { get; set; }
        /// <summary>Esta recepción (IdRecepcion) ya se había terminado: no se ha vuelto a hacer nada.</summary>
        public bool YaEstabaTerminada { get; set; }
        public List<DocumentoRecepcionDTO> Documentos { get; set; } = new List<DocumentoRecepcionDTO>();
        /// <summary>Productos leídos que no estaban en lo esperado: no han entrado.</summary>
        public List<DiferenciaPreparacionDTO> NoEsperados { get; set; } = new List<DiferenciaPreparacionDTO>();
        /// <summary>Lo que tiene que saber quien recibe (y, en compras, lo que se ha avisado a Compras).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
        /// <summary>Lo que ha entrado con un pedido que se había dado por no servido (no es exceso).</summary>
        public List<LineaRecuperadaDTO> Recuperadas { get; set; } = new List<LineaRecuperadaDTO>();
    }
}
