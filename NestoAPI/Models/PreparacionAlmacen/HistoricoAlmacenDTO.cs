using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    // Ariadna#63 (sugerencias 872/873): el histórico de pickings y packings hechos en Ariadna, para saber si algo se picó o
    // se empaquetó, quién y cuándo. Lo ven quien tenga el parámetro VerHistoricoAlmacen = "1" en su propia fila, Dirección e
    // Informática (api/Almacen/Historico).

    /// <summary>GET api/Almacen/Historico/Pickings: un picking con actividad en Ariadna en esas fechas.</summary>
    public class PickingHistoricoDTO
    {
        public int Picking { get; set; }
        /// <summary>La primera lectura del picking en las fechas pedidas (o cuando se terminó, si no hay lecturas).</summary>
        public DateTime Fecha { get; set; }
        /// <summary>Quién lo terminó; si no se ha terminado en Ariadna, quien más ha leído. Sin dominio. Null si no se sabe.</summary>
        public string Usuario { get; set; }
        public List<int> Pedidos { get; set; } = new List<int>();
        /// <summary>Líneas de producto del picking (las de LinPedidoVta con ese picking, en cualquier estado).</summary>
        public int Lineas { get; set; }
        /// <summary>De esas, las recogidas enteras con lo leído en Ariadna.</summary>
        public int LineasRecogidas { get; set; }
        /// <summary>True si se terminó en Ariadna (PreparacionSalidasTerminadas).</summary>
        public bool Terminado { get; set; }
    }

    /// <summary>GET api/Almacen/Historico/Pickings/{picking}.</summary>
    public class PickingHistoricoDetalleDTO
    {
        public int Picking { get; set; }
        /// <summary>La primera lectura (o cuando se terminó). Null si en Ariadna no se hizo nada de él.</summary>
        public DateTime? Fecha { get; set; }
        public string Usuario { get; set; }
        public bool Terminado { get; set; }
        public List<LineaPickingHistoricoDTO> Lineas { get; set; } = new List<LineaPickingHistoricoDTO>();
    }

    /// <summary>
    /// Una línea del picking con lo recogido. En el picking por ola se lee por producto (no por pedido): lo leído de un producto
    /// se reparte entre sus líneas en orden de pedido, y lo dado por falta, entre lo que queda.
    /// </summary>
    public class LineaPickingHistoricoDTO
    {
        public int Pedido { get; set; }
        /// <summary>El número de cliente.</summary>
        public string Cliente { get; set; }
        public string NombreCliente { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
        public int Recogida { get; set; }
        /// <summary>Se dio por falta (todo o parte de lo que no se recogió).</summary>
        public bool Falta { get; set; }
        /// <summary>Quien más ha leído ese producto en el picking (sin dominio). Null si nadie.</summary>
        public string Usuario { get; set; }
        /// <summary>La última lectura de ese producto en el picking. Null si nadie lo leyó.</summary>
        public DateTime? FechaHora { get; set; }
    }

    /// <summary>GET api/Almacen/Historico/Packings: un pedido empaquetado (o empezado) en Ariadna en esas fechas.</summary>
    public class PackingHistoricoDTO
    {
        public int Pedido { get; set; }
        /// <summary>La primera caja o lectura de packing del pedido en las fechas pedidas.</summary>
        public DateTime Fecha { get; set; }
        /// <summary>Quien más cajas ha hecho (o, sin cajas, quien más ha leído). Sin dominio.</summary>
        public string Usuario { get; set; }
        /// <summary>Cajas hechas en Ariadna (EnviosAgenciaBultos, números de bulto distintos).</summary>
        public int Cajas { get; set; }
        /// <summary>Bultos de la etiqueta de agencia del pedido (su último envío). 0 si todavía no tiene etiqueta.</summary>
        public int Bultos { get; set; }
    }

    /// <summary>GET api/Almacen/Historico/Packings/{pedido}.</summary>
    public class PackingHistoricoDetalleDTO
    {
        public int Pedido { get; set; }
        public DateTime? Fecha { get; set; }
        public string Usuario { get; set; }
        /// <summary>Bultos de la etiqueta de agencia (0 sin etiqueta).</summary>
        public int Bultos { get; set; }
        public List<CajaPackingHistoricoDTO> Cajas { get; set; } = new List<CajaPackingHistoricoDTO>();
    }

    /// <summary>Una caja del packing con lo que se metió en ella.</summary>
    public class CajaPackingHistoricoDTO
    {
        public int Bulto { get; set; }
        public int Picking { get; set; }
        /// <summary>Quién hizo la foto de la caja (o, sin foto, quien metió lo último). Sin dominio.</summary>
        public string Usuario { get; set; }
        /// <summary>La hora de la foto (la del móvil) o, sin foto, la de la última lectura.</summary>
        public DateTime? FechaHora { get; set; }
        public bool TieneFoto { get; set; }
        /// <summary>
        /// Ruta relativa a la API del enlace público de la foto (api/Almacen/Fotos/{token}, el mismo de los bultos). Null sin foto
        /// o si los enlaces no están configurados.
        /// </summary>
        public string UrlFoto { get; set; }
        public List<ContenidoCajaHistoricoDTO> Contenido { get; set; } = new List<ContenidoCajaHistoricoDTO>();
    }

    public class ContenidoCajaHistoricoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Unidades metidas (lecturas menos las deshechas).</summary>
        public int Cantidad { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaHora { get; set; }
    }
}
