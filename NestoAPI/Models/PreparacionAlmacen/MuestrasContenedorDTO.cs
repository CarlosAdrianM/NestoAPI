using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#884: respuesta de POST api/Productos/Muestras/CodigoInterno: el código interno del 29 dado (o que se daría, en el
    /// ensayo) a las muestras activas que no tienen ningún código de barras.
    /// </summary>
    public class ResultadoCodigoInternoMuestrasDTO
    {
        public bool Ensayo { get; set; }
        /// <summary>Las muestras activas sin ningún código (las que entran en el bloque).</summary>
        public int Total { get; set; }
        /// <summary>A cuántas se les ha dado ahora (0 en el ensayo).</summary>
        public int Asignados { get; set; }
        /// <summary>Las que no se han podido hacer (cada una con su Error).</summary>
        public int Errores { get; set; }
        public string Mensaje { get; set; }
        public List<MuestraCodigoInternoDTO> Muestras { get; set; } = new List<MuestraCodigoInternoDTO>();
    }

    public class MuestraCodigoInternoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>El nombre de la familia (la marca).</summary>
        public string Familia { get; set; }
        /// <summary>
        /// El código interno dado. En el ensayo, el que le tocaría (290 + referencia); null si le tocaría uno del contador (299…),
        /// que no se sabe hasta darlo.
        /// </summary>
        public string Codigo { get; set; }
        /// <summary>Se le ha dado ahora (false en el ensayo o si otra petición se lo acababa de dar).</summary>
        public bool Asignado { get; set; }
        /// <summary>Por qué no se ha podido; null si bien.</summary>
        public string Error { get; set; }
    }

    /// <summary>NestoAPI#884: qué etiquetas de cajón o gaveta de muestras imprimir (POST api/Almacen/EtiquetasProducto/Muestras).</summary>
    public class PeticionEtiquetasMuestrasDTO
    {
        /// <summary>Obligatorio: ALG (por hueco), REI o ALC (por familia y nombre).</summary>
        public string Almacen { get; set; }
        /// <summary>Opcional: la familia (su código o su nombre, «Lisap»).</summary>
        public string Familia { get; set; }
        /// <summary>Solo las muestras con stock en el almacén.</summary>
        public bool SoloConStock { get; set; }
        /// <summary>Opcional: solo estas referencias (para reimprimir unas pocas).</summary>
        public List<string> Productos { get; set; }
        /// <summary>Opcional (Carlos 25/11/26): la impresora (\\servidor\cola). Sin ella, la del almacén pedido.</summary>
        public string Impresora { get; set; }
    }

    public class ResultadoEtiquetasMuestrasDTO
    {
        public string Almacen { get; set; }
        public bool Ensayo { get; set; }
        /// <summary>Cuántas etiquetas salen (o saldrían).</summary>
        public int Total { get; set; }
        /// <summary>0 en el ensayo.</summary>
        public int Impresas { get; set; }
        /// <summary>Donde salen (o saldrían): la pedida o, sin ella, la del almacén (ParametrosUsuario «(defecto)» ImpresoraEtiquetas.{almacén}).</summary>
        public string Impresora { get; set; }
        /// <summary>De dónde sale la impresora: «Pedida» o «Almacen».</summary>
        public string OrigenImpresora { get; set; }
        /// <summary>A cuántas muestras se les ha dado ahora el código interno (sin ningún código, al imprimir).</summary>
        public int CodigosAsignados { get; set; }
        /// <summary>Cuántas muestras no tienen código: al imprimir de verdad se les da el interno del 29.</summary>
        public int SinCodigo { get; set; }
        public string Mensaje { get; set; }
        /// <summary>En el orden en que salen (para pegarlas de una vez).</summary>
        public List<EtiquetaMuestraDTO> Etiquetas { get; set; } = new List<EtiquetaMuestraDTO>();
        /// <summary>De las referencias pedidas, las que no son muestras activas (no salen).</summary>
        public List<string> NoSonMuestras { get; set; } = new List<string>();
    }

    public class EtiquetaMuestraDTO
    {
        public int Orden { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>El nombre de la familia (la marca).</summary>
        public string Familia { get; set; }
        /// <summary>En Algete, el hueco donde se pega («003/001/002»); null si la muestra no tiene hueco (o es una tienda).</summary>
        public string Hueco { get; set; }
        /// <summary>Stock en el almacén (empresas 1 y 3).</summary>
        public int Stock { get; set; }
        /// <summary>El EAN-13 de la etiqueta: el del fabricante o el interno del 29. Null si aún no tiene (se le da al imprimir).</summary>
        public string CodigoBarras { get; set; }
        public bool CodigoInterno { get; set; }
        /// <summary>Se le acaba de dar el interno al imprimir.</summary>
        public bool CodigoAsignado { get; set; }
        /// <summary>El texto de la etiqueta («MUESTRA · Ref. 17404» y el nombre en grande).</summary>
        public List<string> Lineas { get; set; } = new List<string>();
    }
}
