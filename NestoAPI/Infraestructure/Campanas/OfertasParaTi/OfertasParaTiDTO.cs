using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>«Ofertas para ti»: por qué se le enseña una oferta a un cliente (va tal cual en el JSON).</summary>
    public static class MotivosOfertaParaTi
    {
        /// <summary>Lo compra a menudo (2 días o más en 12 meses) y, por su ritmo, todavía no le toca.</summary>
        public const string LO_SUELES_PEDIR = "LoSuelesPedir";
        /// <summary>Lo compra a menudo y ya ha pasado el 60 % de su ciclo desde la última vez.</summary>
        public const string TE_TOCA = "TeToca";
        /// <summary>De una de las familias (marcas) que más compra en 24 meses.</summary>
        public const string DE_TUS_MARCAS = "DeTusMarcas";
        /// <summary>Lo demás: por descuento y por lo que lo piden otros profesionales.</summary>
        public const string TE_PUEDE_INTERESAR = "TePuedeInteresar";
    }

    /// <summary>Una fila vigente de una campaña (DescuentosProducto de tarifa, por producto) con la ficha del producto.</summary>
    public class FilaCampanaOferta
    {
        public string Campana { get; set; }
        public string Producto { get; set; }
        public decimal Descuento { get; set; }
        public decimal? Precio { get; set; }
        public byte AudienciaOferta { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string NombreFamilia { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public decimal? PVP { get; set; }
        public short? Estado { get; set; }
    }

    /// <summary>Un producto en oferta (una sola fila por producto: si está en dos campañas, la de más descuento).</summary>
    public class CandidataOferta
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string NombreFamilia { get; set; }
        public string Campana { get; set; }
        /// <summary>El de la fila de la campaña, en tanto por uno (con precio fijo, 1 − Precio/PVP).</summary>
        public decimal Descuento { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        /// <summary>Clientes distintos que lo compraron en 12 meses (SugerenciasProductosPopularidad; 0 si no hay tabla).</summary>
        public int Popularidad { get; set; }
    }

    /// <summary>Lo que hace falta de un cliente para elegir sus ofertas.</summary>
    public class HistorialClienteOfertas
    {
        public string Cliente { get; set; }
        /// <summary>Compras (albaranes) de 24 meses, por producto y día.</summary>
        public List<CorreosPostCompra.CompraDiaReposicion> Compras { get; set; } = new List<CorreosPostCompra.CompraDiaReposicion>();
        /// <summary>Productos que tiene en un pedido pendiente o en curso (-1..1).</summary>
        public ISet<string> Pendientes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Producto → familia de lo que ha comprado (para «De tus marcas»).</summary>
        public IDictionary<string, string> FamiliaDe { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Una candidata ya mirada contra el historial del cliente, en el orden de la regla.</summary>
    public class CandidataEvaluada
    {
        public CandidataOferta Candidata { get; set; }
        public string Motivo { get; set; }
        public string Texto { get; set; }
        public int CantidadSugerida { get; set; } = 1;
        /// <summary>1 le toca, 2 habitual, 3 de sus marcas, 4 lo demás.</summary>
        public int Grupo { get; set; }
        public bool Habitual => Grupo == 1 || Grupo == 2;
    }

    /// <summary>Una oferta elegida para el cliente (antes de precios y fotos).</summary>
    public class OfertaElegida
    {
        public CandidataOferta Candidata { get; set; }
        public string Motivo { get; set; }
        public string Texto { get; set; }
        public int CantidadSugerida { get; set; } = 1;
        public bool Habitual { get; set; }
        /// <summary>La P1 del modelo 1, si se ha usado. Null: ordenada por la regla.</summary>
        public float? Probabilidad { get; set; }
    }

    /// <summary>Las ofertas de un cliente y cómo se han ordenado.</summary>
    public class ResultadoOfertasCliente
    {
        public string Cliente { get; set; }
        public List<OfertaElegida> Ofertas { get; set; } = new List<OfertaElegida>();
        /// <summary>«m1-…» si las ha ordenado el modelo 1; null si la regla.</summary>
        public string VersionModelo { get; set; }
    }

    // ------------------------------------------------------------------------------------------------
    // GET api/Pedidos/Cliente/Ofertas

    /// <summary>La respuesta del endpoint de la app (siempre 200).</summary>
    public class OfertasParaTiRespuestaDTO
    {
        public bool HayOfertas { get; set; }
        public string Campana { get; set; }
        public string Titulo { get; set; }
        /// <summary>«yyyy-MM-dd»; null si la campaña no tiene fin.</summary>
        public string FechaHasta { get; set; }
        public string TextoHasta { get; set; }
        /// <summary>Quien la pide no ve los precios (cargo 30): las ofertas van sin importes.</summary>
        public bool PrecioOculto { get; set; }
        /// <summary>Quien la pide no ve los descuentos (cargo 31): va la tarifa en Precio, sin PrecioAntes ni Descuento.</summary>
        public bool DescuentoOculto { get; set; }
        public List<OfertaParaTiDTO> Ofertas { get; set; } = new List<OfertaParaTiDTO>();
    }

    public class OfertaParaTiDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string UrlFoto { get; set; }
        public string Familia { get; set; }
        /// <summary>La campaña de esta oferta (sin campaña pedida, cada oferta puede ser de una).</summary>
        public string Campana { get; set; }
        /// <summary>Lo que pagaría sin la campaña (precio unitario neto, sin IVA). Null si no lo ve.</summary>
        public decimal? PrecioAntes { get; set; }
        /// <summary>Lo que le da el motor de precios hoy (precio unitario neto, sin IVA). Null si no ve precios.</summary>
        public decimal? Precio { get; set; }
        /// <summary>1 − Precio/PrecioAntes, en tanto por uno. Null si no ve descuentos.</summary>
        public decimal? Descuento { get; set; }
        public int CantidadSugerida { get; set; }
        public string Motivo { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>Los dos precios de una oferta para un cliente (los del motor de siempre).</summary>
    public class PreciosOferta
    {
        /// <summary>Precio unitario efectivo hoy, con la campaña.</summary>
        public decimal Precio { get; set; }
        /// <summary>Precio unitario efectivo sin la campaña.</summary>
        public decimal PrecioAntes { get; set; }
        /// <summary>La tarifa profesional (Productos.PVP).</summary>
        public decimal Tarifa { get; set; }
    }

    // ------------------------------------------------------------------------------------------------
    // POST api/Campanas/Push

    /// <summary>
    /// La petición. Carlos 25/11/26: los push de campaña NUNCA tocan el carrito: ya no hay «Modo» (si llega en el JSON, se ignora: no es
    /// una propiedad) y la push es siempre tipo «ofertas».
    /// </summary>
    public class PeticionCampanaPushDTO
    {
        public string Campana { get; set; }
        public string Titulo { get; set; }
        public string Cuerpo { get; set; }
        public string ImagenUrl { get; set; }
        public int? Maximo { get; set; }
        public List<string> SoloClientes { get; set; } = new List<string>();
        public List<string> ExcluirClientes { get; set; } = new List<string>();
        public int? DiasSinRepetir { get; set; }
        public bool? IncluirSinOfertas { get; set; }
        public string CuerpoSinOfertas { get; set; }
    }

    public class SaltadoPushCampanaDTO
    {
        public string Cliente { get; set; }
        public string Motivo { get; set; }
    }

    public class MensajePushCampanaDTO
    {
        public string Cliente { get; set; }
        public string Titulo { get; set; }
        public string Cuerpo { get; set; }
        public List<string> Productos { get; set; } = new List<string>();
        public string ImagenUrl { get; set; }
        /// <summary>Dispositivos a los que ha salido (0 en ensayo).</summary>
        public int Enviados { get; set; }
    }

    public class RespuestaCampanaPushDTO
    {
        public bool Ensayo { get; set; }
        public string Campana { get; set; }
        public int Destinatarios { get; set; }
        public int ConOfertas { get; set; }
        public int SinOfertas { get; set; }
        public List<SaltadoPushCampanaDTO> Saltados { get; set; } = new List<SaltadoPushCampanaDTO>();
        public List<MensajePushCampanaDTO> Mensajes { get; set; } = new List<MensajePushCampanaDTO>();
    }
}
