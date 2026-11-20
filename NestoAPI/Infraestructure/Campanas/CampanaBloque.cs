using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Campanas
{
    /// <summary>
    /// POST api/Campanas/Bloque (Black Friday 2026): una campaña de descuentos de muchas referencias de una vez, creada como
    /// filas POR PRODUCTO de DescuentosProducto (la fila por familia la pisan las de cliente por familia/grupo y las de
    /// familia+grupo+subgrupo; la de producto gana si es mayor). Descuentos en TANTO POR UNO, como en la tabla y en
    /// <c>POST api/Campanas</c> (0,20 = 20 %).
    /// </summary>
    public class PeticionCampanaBloqueDTO
    {
        /// <summary>Nombre de la campaña (DescuentosProducto.Campana, 50 como mucho). Es la etiqueta para borrarla o cerrarla entera.</summary>
        public string Nombre { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        /// <summary>Sin ella, la 1. Solo vale la 1: el mantenimiento de campañas y el job de vigencia solo miran esa.</summary>
        public string Empresa { get; set; }
        /// <summary>Audiencia por defecto de reglas y excepciones que no traen la suya: 0 no va a la web, 1 solo profesionales, 2 ambos.</summary>
        public byte? Audiencia { get; set; }
        public List<ReglaCampanaBloqueDTO> Reglas { get; set; }
        /// <summary>Productos con su propio descuento: mandan sobre la regla de su familia (y sobre sus exclusiones de texto, grupo y subgrupo).</summary>
        public List<ExcepcionCampanaBloqueDTO> Excepciones { get; set; }
        /// <summary>Referencias que no entran nunca, sea cual sea su regla.</summary>
        public List<string> ProductosExcluidos { get; set; }
        /// <summary>Las fichas con «Aplicar Dto» = 0 que entren van a dbo.CampanasAplicarDto por las mismas fechas. Si es false, se quedan fuera.</summary>
        public bool DesbloquearAplicarDto { get; set; }
        /// <summary>Todos los que entren llevan condición de contado (dbo.CampanasCondicionContado), salvo que su regla o excepción diga otra cosa.</summary>
        public bool CondicionContado { get; set; }
        /// <summary>Texto propio del aviso de contado (500 como mucho); null = el de siempre.</summary>
        public string TextoAvisoContado { get; set; }
        /// <summary>A los distribuidores el descuento de la campaña se les suma ENCIMA del suyo (dbo.CampanasAcumulanDistribuidores, mismas fechas).</summary>
        public bool AcumulaParaDistribuidores { get; set; }
    }

    public class ReglaCampanaBloqueDTO
    {
        public string Familia { get; set; }
        /// <summary>En tanto por uno: 0,20 = 20 %.</summary>
        public decimal Descuento { get; set; }
        /// <summary>Null = la de la petición.</summary>
        public byte? Audiencia { get; set; }
        public List<string> ProductosExcluidos { get; set; }
        /// <summary>Palabras (o grupos de palabras) del nombre, sin mayúsculas ni tildes y palabra entera: «pack», «lote».</summary>
        public List<string> TextosExcluidos { get; set; }
        public List<string> GruposExcluidos { get; set; }
        /// <summary>«OUT» (de cualquier grupo) o «COS/OUT» (de ese grupo).</summary>
        public List<string> SubgruposExcluidos { get; set; }
        /// <summary>Null = la de la petición.</summary>
        public bool? CondicionContado { get; set; }
    }

    public class ExcepcionCampanaBloqueDTO
    {
        public string Producto { get; set; }
        /// <summary>En tanto por uno: 0,20 = 20 %.</summary>
        public decimal Descuento { get; set; }
        public byte? Audiencia { get; set; }
        public bool? CondicionContado { get; set; }
    }

    /// <summary>Lo que hace falta de la ficha (una consulta para todas).</summary>
    public class ProductoCandidatoCampana
    {
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public decimal? PVP { get; set; }
        public bool AplicarDto { get; set; }
        public short? Estado { get; set; }
    }

    /// <summary>
    /// Una fila de TARIFA de DescuentosProducto de un producto candidato (sin cliente ni proveedor, desde 1 unidad): las de la
    /// misma campaña (cualquier fecha) y las de otras que se solapan con las fechas pedidas.
    /// </summary>
    public class FilaExistenteCampana
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        public decimal Descuento { get; set; }
        public decimal? Precio { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Campana { get; set; }
        public byte Audiencia { get; set; }
    }

    public enum EstadoLineaCampanaBloque
    {
        /// <summary>Se crea su fila.</summary>
        Entra,
        /// <summary>Ya tiene su fila de ESTA campaña (repetir la petición no duplica).</summary>
        YaEstaba,
        /// <summary>No entra: ver Motivo.</summary>
        Fuera
    }

    public static class MotivosCampanaBloque
    {
        public const string NO_EXISTE = "NoExiste";
        public const string ESTADO = "EstadoNoVivo";
        public const string MATERIA_PRIMA = "MateriaPrima";
        public const string MUESTRA = "Muestra";
        public const string EXCLUIDO_REFERENCIA = "ExcluidoPorReferencia";
        public const string EXCLUIDO_TEXTO = "ExcluidoPorTexto";
        public const string EXCLUIDO_GRUPO = "ExcluidoPorGrupo";
        public const string EXCLUIDO_SUBGRUPO = "ExcluidoPorSubgrupo";
        public const string SIN_PVP = "SinPvp";
        public const string FICHA_BLOQUEADA = "FichaBloqueada";
        public const string YA_TIENE_MEJOR = "YaTieneDescuentoMejorOIgual";

        public static bool EsSolape(string motivo) => motivo == YA_TIENE_MEJOR;
    }

    public class LineaCampanaBloqueDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public decimal? PVP { get; set; }
        /// <summary>El descuento que le tocaría (tanto por uno).</summary>
        public decimal Descuento { get; set; }
        /// <summary>PVP × (1 − descuento), a 2 decimales.</summary>
        public decimal? PrecioResultante { get; set; }
        public byte Audiencia { get; set; }
        /// <summary>«Regla» o «Excepción».</summary>
        public string Origen { get; set; }
        public EstadoLineaCampanaBloque Estado { get; set; }
        /// <summary>Solo si queda fuera (<see cref="MotivosCampanaBloque"/>).</summary>
        public string Motivo { get; set; }
        /// <summary>El motivo explicado (y en las de solape, la fila que ya hay).</summary>
        public string Detalle { get; set; }
        /// <summary>Su ficha tiene «Aplicar Dto» = 0 y se desbloquea por las fechas de la campaña.</summary>
        public bool DesbloquearFicha { get; set; }
        public bool CondicionContado { get; set; }
        /// <summary>Nº Orden de la fila de DescuentosProducto que ya había (ya estaba o solape).</summary>
        public int? FilaExistente { get; set; }
        /// <summary>Tiene otra fila de tarifa de producto vigente esas fechas: fuera si es mejor o igual, dentro (y la otra se queda) si es peor.</summary>
        public bool Solape { get; set; }
    }

    public class TotalesCampanaBloqueDTO
    {
        /// <summary>Null en el total general.</summary>
        public string Familia { get; set; }
        public int Productos { get; set; }
        public int Entran { get; set; }
        public int YaEstaban { get; set; }
        public int Fuera { get; set; }
        /// <summary>Los que quedan fuera, por motivo.</summary>
        public Dictionary<string, int> FueraPorMotivo { get; set; } = new Dictionary<string, int>();
        /// <summary>De los que entran o ya estaban, cuántas fichas se desbloquean.</summary>
        public int FichasDesbloqueadas { get; set; }
        /// <summary>Productos con otra fila de tarifa de producto vigente esas fechas (fuera si es mejor o igual; dentro si es peor).</summary>
        public int Solapes { get; set; }
        public int CondicionContado { get; set; }
    }

    public class ResultadoCalculoCampanaBloque
    {
        public List<LineaCampanaBloqueDTO> Lineas { get; set; } = new List<LineaCampanaBloqueDTO>();
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>
    /// LA REGLA del alta en bloque (pura). Por cada producto candidato, en este orden: ya tiene la fila de ESTA campaña (ya
    /// estaba) · ficha que no existe · estado no vivo (Estado null o &lt; 0) · materia prima (grupo MTP) · muestra (subgrupo MMP) ·
    /// excluido por referencia (global o de su regla) · y, si entra por la regla de su familia (no por excepción), excluido
    /// por grupo, subgrupo o texto · sin PVP · ficha bloqueada («Aplicar Dto» = 0) sin desbloqueo · solape con otra fila de
    /// tarifa del producto vigente esas fechas · entra.
    /// <para><b>Solapes (Carlos 20/11/26: «queda el mejor de los dos»):</b> nunca se pisa ni se borra nada. Si el producto ya
    /// tiene otra fila de tarifa de producto (sin cliente ni proveedor, desde 1 unidad) que se solapa con las fechas y es
    /// MEJOR O IGUAL para el cliente (precio efectivo: el precio especial si baja, o el PVP con su descuento), no se crea la de
    /// la campaña (<see cref="MotivosCampanaBloque.YA_TIENE_MEJOR"/>); si es PEOR, se crea igual y la línea va marcada como
    /// solape (<see cref="LineaCampanaBloqueDTO.Solape"/>). OJO (pendiente de decidir, no se ha tocado el motor): con dos
    /// filas de producto de la misma CantidadMínima, <c>GestorPrecios.calcularDescuentoProducto</c> coge la primera que le da
    /// SQL (OrderByDescending(CantidadMínima).FirstOrDefault, sin desempate), así que «el mejor de los dos» solo está
    /// garantizado si se le añade el desempate (mayor Descuento; y en el precio, la fila con Precio más bajo).</para>
    /// </summary>
    public static class ReglaCampanaBloque
    {
        public const int LONGITUD_NOMBRE = 50;
        public const int LONGITUD_TEXTO_AVISO = 500;
        /// <summary>Para no pasar de los 2.100 parámetros de SQL Server en la lectura.</summary>
        public const int MAXIMO_REGLAS = 300;
        public const int MAXIMO_EXCEPCIONES = 1500;
        public const string ORIGEN_REGLA = "Regla";
        public const string ORIGEN_EXCEPCION = "Excepción";

        /// <summary>El primer error de la petición (de usted), o null si vale.</summary>
        public static string Validar(PeticionCampanaBloqueDTO p, DateTime hoy)
        {
            if (p == null)
            {
                return "No ha llegado ninguna campaña.";
            }
            if (string.IsNullOrWhiteSpace(p.Nombre))
            {
                return "Falta el nombre de la campaña: es lo que permite borrarla o cerrarla entera después.";
            }
            if (p.Nombre.Trim().Length > LONGITUD_NOMBRE)
            {
                return $"El nombre de la campaña no puede pasar de {LONGITUD_NOMBRE} caracteres.";
            }
            if (!p.FechaDesde.HasValue || !p.FechaHasta.HasValue)
            {
                return "Una campaña en bloque necesita fecha de inicio y de fin.";
            }
            string errorRango = Vigencia.ErrorDeRango(p.FechaDesde, p.FechaHasta);
            if (errorRango != null)
            {
                return errorRango + ".";
            }
            if (p.FechaHasta.Value.Date < hoy.Date)
            {
                return "La campaña ya ha terminado: la fecha de fin es anterior a hoy.";
            }
            if (!string.IsNullOrWhiteSpace(p.Empresa) && p.Empresa.Trim() != Constantes.Empresas.EMPRESA_POR_DEFECTO)
            {
                return $"Las campañas solo se dan de alta en la empresa {Constantes.Empresas.EMPRESA_POR_DEFECTO}: el mantenimiento de campañas y su publicación en la tienda solo miran esa.";
            }
            List<ReglaCampanaBloqueDTO> reglas = p.Reglas ?? new List<ReglaCampanaBloqueDTO>();
            List<ExcepcionCampanaBloqueDTO> excepciones = p.Excepciones ?? new List<ExcepcionCampanaBloqueDTO>();
            if (!reglas.Any() && !excepciones.Any())
            {
                return "La campaña no tiene ninguna regla por familia ni ninguna excepción por producto.";
            }
            if (reglas.Count > MAXIMO_REGLAS)
            {
                return $"Como mucho {MAXIMO_REGLAS} reglas por familia en una petición.";
            }
            if (excepciones.Count > MAXIMO_EXCEPCIONES)
            {
                return $"Como mucho {MAXIMO_EXCEPCIONES} excepciones por producto en una petición.";
            }
            if (p.Audiencia.HasValue && p.Audiencia.Value > 2)
            {
                return MensajeAudiencia;
            }

            var familias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReglaCampanaBloqueDTO r in reglas)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Familia))
                {
                    return "Hay una regla sin familia.";
                }
                string familia = r.Familia.Trim();
                if (!familias.Add(familia))
                {
                    return $"La familia {familia} está en dos reglas: póngala solo en una.";
                }
                string error = ErrorDescuento(r.Descuento, $"la familia {familia}") ?? ErrorAudiencia(r.Audiencia ?? p.Audiencia, $"la familia {familia}");
                if (error != null)
                {
                    return error;
                }
                if ((r.TextosExcluidos ?? new List<string>()).Any(t => string.IsNullOrWhiteSpace(NormalizarTexto(t))))
                {
                    return $"La regla de la familia {familia} tiene un texto excluido vacío.";
                }
            }

            HashSet<string> excluidos = Conjunto(p.ProductosExcluidos);
            var productos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ExcepcionCampanaBloqueDTO e in excepciones)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.Producto))
                {
                    return "Hay una excepción sin producto.";
                }
                string producto = e.Producto.Trim();
                if (!productos.Add(producto))
                {
                    return $"El producto {producto} está dos veces en las excepciones.";
                }
                if (excluidos.Contains(producto) || reglas.Any(r => Conjunto(r.ProductosExcluidos).Contains(producto)))
                {
                    return $"El producto {producto} está a la vez en las excepciones y en los excluidos: decida si entra o no.";
                }
                string error = ErrorDescuento(e.Descuento, $"el producto {producto}") ?? ErrorAudiencia(e.Audiencia ?? p.Audiencia, $"el producto {producto}");
                if (error != null)
                {
                    return error;
                }
            }

            if (!string.IsNullOrWhiteSpace(p.TextoAvisoContado) && p.TextoAvisoContado.Trim().Length > LONGITUD_TEXTO_AVISO)
            {
                return $"El texto del aviso de contado no puede pasar de {LONGITUD_TEXTO_AVISO} caracteres.";
            }
            return null;
        }

        private const string MensajeAudiencia = "La audiencia solo puede ser 0 (no va a la web), 1 (solo profesionales) o 2 (ambos). " +
            "El 3 (solo público) está prohibido: el motor de precios no mira la audiencia y le descontaría igual al profesional en el pedido.";

        private static string ErrorDescuento(decimal descuento, string deQue) =>
            descuento <= 0M || descuento >= 1M
                ? $"El descuento de {deQue} va en tanto por uno, mayor que 0 y menor que 1 (0,20 = 20 %)."
                : null;

        private static string ErrorAudiencia(byte? audiencia, string deQue)
        {
            if (!audiencia.HasValue)
            {
                return $"Falta la audiencia de {deQue} (0 no va a la web, 1 solo profesionales, 2 ambos): póngala en la regla o en la petición.";
            }
            return audiencia.Value > 2 ? MensajeAudiencia : null;
        }

        /// <summary>Las familias de las reglas (recortadas, sin repetir).</summary>
        public static List<string> Familias(PeticionCampanaBloqueDTO p) =>
            (p?.Reglas ?? new List<ReglaCampanaBloqueDTO>()).Where(r => !string.IsNullOrWhiteSpace(r?.Familia))
                .Select(r => r.Familia.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Los productos de las excepciones (recortados, sin repetir).</summary>
        public static List<string> ProductosExcepcion(PeticionCampanaBloqueDTO p) =>
            (p?.Excepciones ?? new List<ExcepcionCampanaBloqueDTO>()).Where(e => !string.IsNullOrWhiteSpace(e?.Producto))
                .Select(e => e.Producto.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Las filas a crear y, de cada producto que no entra, por qué. La petición tiene que estar validada.</summary>
        public static ResultadoCalculoCampanaBloque Calcular(PeticionCampanaBloqueDTO p, IEnumerable<ProductoCandidatoCampana> candidatos,
            IEnumerable<FilaExistenteCampana> existentes)
        {
            var resultado = new ResultadoCalculoCampanaBloque();
            string campana = p.Nombre.Trim();
            DateTime desde = p.FechaDesde.Value.Date;
            DateTime hasta = p.FechaHasta.Value.Date;

            Dictionary<string, ProductoCandidatoCampana> fichas = (candidatos ?? Enumerable.Empty<ProductoCandidatoCampana>())
                .Where(c => !string.IsNullOrWhiteSpace(c?.Numero))
                .GroupBy(c => c.Numero.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            ILookup<string, FilaExistenteCampana> filasPorProducto = (existentes ?? Enumerable.Empty<FilaExistenteCampana>())
                .Where(f => !string.IsNullOrWhiteSpace(f?.Producto))
                .ToLookup(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ReglaCampanaBloqueDTO> reglas = (p.Reglas ?? new List<ReglaCampanaBloqueDTO>())
                .ToDictionary(r => r.Familia.Trim(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ExcepcionCampanaBloqueDTO> excepciones = (p.Excepciones ?? new List<ExcepcionCampanaBloqueDTO>())
                .ToDictionary(e => e.Producto.Trim(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> excluidosGlobales = Conjunto(p.ProductosExcluidos);

            // Las excepciones primero (en su orden), luego cada familia con sus productos por referencia.
            foreach (ExcepcionCampanaBloqueDTO e in p.Excepciones ?? new List<ExcepcionCampanaBloqueDTO>())
            {
                string numero = e.Producto.Trim();
                _ = fichas.TryGetValue(numero, out ProductoCandidatoCampana ficha);
                // La excepción manda sobre la regla de su familia: de esa regla no se mira nada.
                resultado.Lineas.Add(Evaluar(numero, ficha, ORIGEN_EXCEPCION, e.Descuento, e.Audiencia ?? p.Audiencia ?? 0,
                    e.CondicionContado ?? p.CondicionContado, null, excluidosGlobales, p, campana, desde, hasta, filasPorProducto[numero]));
            }

            foreach (ReglaCampanaBloqueDTO r in p.Reglas ?? new List<ReglaCampanaBloqueDTO>())
            {
                string familia = r.Familia.Trim();
                List<ProductoCandidatoCampana> deLaFamilia = fichas.Values
                    .Where(f => string.Equals(f.Familia?.Trim(), familia, StringComparison.OrdinalIgnoreCase) && !excepciones.ContainsKey(f.Numero.Trim()))
                    .OrderBy(f => f.Numero.Trim(), StringComparer.Ordinal)
                    .ToList();
                bool algunoDeLaFamilia = fichas.Values.Any(f => string.Equals(f.Familia?.Trim(), familia, StringComparison.OrdinalIgnoreCase));
                if (!algunoDeLaFamilia)
                {
                    resultado.Avisos.Add($"La familia {familia} no tiene ningún producto: compruebe que está bien escrita.");
                }
                foreach (ProductoCandidatoCampana ficha in deLaFamilia)
                {
                    string numero = ficha.Numero.Trim();
                    resultado.Lineas.Add(Evaluar(numero, ficha, ORIGEN_REGLA, r.Descuento, r.Audiencia ?? p.Audiencia ?? 0,
                        r.CondicionContado ?? p.CondicionContado, r, excluidosGlobales, p, campana, desde, hasta, filasPorProducto[numero]));
                }
            }
            return resultado;
        }

        private static LineaCampanaBloqueDTO Evaluar(string numero, ProductoCandidatoCampana ficha, string origen, decimal descuento, byte audiencia,
            bool contado, ReglaCampanaBloqueDTO regla, HashSet<string> excluidosGlobales, PeticionCampanaBloqueDTO p, string campana,
            DateTime desde, DateTime hasta, IEnumerable<FilaExistenteCampana> filasDelProducto)
        {
            var linea = new LineaCampanaBloqueDTO
            {
                Producto = numero,
                Nombre = ficha?.Nombre?.Trim(),
                Familia = ficha?.Familia?.Trim(),
                Grupo = ficha?.Grupo?.Trim(),
                SubGrupo = ficha?.SubGrupo?.Trim(),
                PVP = ficha?.PVP,
                Descuento = descuento,
                PrecioResultante = ficha?.PVP.HasValue == true ? RoundingHelper.DosDecimalesRound(ficha.PVP.Value * (1M - descuento)) : (decimal?)null,
                Audiencia = audiencia,
                Origen = origen
            };
            List<FilaExistenteCampana> filas = filasDelProducto?.ToList() ?? new List<FilaExistenteCampana>();

            FilaExistenteCampana suya = filas.FirstOrDefault(f => string.Equals(f.Campana?.Trim(), campana, StringComparison.OrdinalIgnoreCase));
            if (suya != null)
            {
                linea.Estado = EstadoLineaCampanaBloque.YaEstaba;
                linea.FilaExistente = suya.Id;
                linea.Detalle = $"Ya tiene su fila de esta campaña (nº {suya.Id}, {Porcentaje(suya.Descuento)}): no se duplica.";
                linea.DesbloquearFicha = ficha != null && !ficha.AplicarDto && p.DesbloquearAplicarDto;
                linea.CondicionContado = contado;
                return linea;
            }

            string motivo = null, detalle = null;
            if (ficha == null)
            {
                (motivo, detalle) = (MotivosCampanaBloque.NO_EXISTE, $"No existe el producto {numero} en la empresa {Constantes.Empresas.EMPRESA_POR_DEFECTO}.");
            }
            else if (!ficha.Estado.HasValue || ficha.Estado.Value < 0)
            {
                (motivo, detalle) = (MotivosCampanaBloque.ESTADO, $"Estado {(ficha.Estado.HasValue ? ficha.Estado.Value.ToString(CultureInfo.InvariantCulture) : "sin estado")}: no está vivo.");
            }
            else if (Igual(ficha.Grupo, Constantes.Productos.GRUPO_MATERIAS_PRIMAS))
            {
                (motivo, detalle) = (MotivosCampanaBloque.MATERIA_PRIMA, "Materia prima (grupo MTP): nunca entra.");
            }
            else if (Igual(ficha.SubGrupo, Constantes.Productos.SUBGRUPO_MUESTRAS))
            {
                (motivo, detalle) = (MotivosCampanaBloque.MUESTRA, "Muestra (subgrupo MMP): ya va al 100 %.");
            }
            else if (excluidosGlobales.Contains(numero) || Conjunto(regla?.ProductosExcluidos).Contains(numero))
            {
                (motivo, detalle) = (MotivosCampanaBloque.EXCLUIDO_REFERENCIA, "Excluido por referencia.");
            }
            else if (origen == ORIGEN_REGLA && Conjunto(regla?.GruposExcluidos).Contains(ficha.Grupo?.Trim() ?? string.Empty))
            {
                (motivo, detalle) = (MotivosCampanaBloque.EXCLUIDO_GRUPO, $"Excluido por el grupo {ficha.Grupo?.Trim()}.");
            }
            else if (origen == ORIGEN_REGLA && SubgrupoExcluido(regla?.SubgruposExcluidos, ficha.Grupo, ficha.SubGrupo))
            {
                (motivo, detalle) = (MotivosCampanaBloque.EXCLUIDO_SUBGRUPO, $"Excluido por el subgrupo {ficha.Grupo?.Trim()}/{ficha.SubGrupo?.Trim()}.");
            }
            else if (origen == ORIGEN_REGLA && TextoExcluido(regla?.TextosExcluidos, ficha.Nombre) is string texto)
            {
                (motivo, detalle) = (MotivosCampanaBloque.EXCLUIDO_TEXTO, $"Excluido por el texto «{texto}» del nombre.");
            }
            else if (!ficha.PVP.HasValue || ficha.PVP.Value <= 0M)
            {
                (motivo, detalle) = (MotivosCampanaBloque.SIN_PVP, "Sin PVP: no hay precio sobre el que descontar.");
            }
            else if (!ficha.AplicarDto && !p.DesbloquearAplicarDto)
            {
                (motivo, detalle) = (MotivosCampanaBloque.FICHA_BLOQUEADA, "Su ficha tiene «Aplicar Dto» = 0 y no se ha pedido desbloquearla: el motor no le aplicaría el descuento.");
            }
            else
            {
                List<FilaExistenteCampana> solapes = filas
                    .Where(f => Vigencia.SeSolapan(f.FechaDesde, f.FechaHasta, desde, hasta))
                    .ToList();
                if (solapes.Any())
                {
                    decimal precioCampana = ficha.PVP.Value * (1M - descuento);
                    FilaExistenteCampana mejor = solapes.OrderBy(f => PrecioEfectivo(f, ficha.PVP.Value)).First();
                    bool mejorOIgual = PrecioEfectivo(mejor, ficha.PVP.Value) <= precioCampana;
                    linea.FilaExistente = mejor.Id;
                    linea.Solape = true;
                    if (mejorOIgual)
                    {
                        motivo = MotivosCampanaBloque.YA_TIENE_MEJOR;
                        detalle = $"Ya tiene {DescribirFila(mejor)} esas fechas, igual o mejor: no se crea.";
                    }
                    else
                    {
                        linea.Detalle = $"Tiene {DescribirFila(mejor)} esas fechas, peor que la de la campaña: se crea y el motor se queda con la mejor; la otra no se toca.";
                    }
                }
            }

            if (motivo != null)
            {
                linea.Estado = EstadoLineaCampanaBloque.Fuera;
                linea.Motivo = motivo;
                linea.Detalle = detalle;
                return linea;
            }
            linea.Estado = EstadoLineaCampanaBloque.Entra;
            linea.DesbloquearFicha = !ficha.AplicarDto;
            linea.CondicionContado = contado;
            return linea;
        }

        /// <summary>Lo que cobraría el motor con esa fila sola: el precio especial si baja del PVP, o el PVP con su descuento (lo menor).</summary>
        internal static decimal PrecioEfectivo(FilaExistenteCampana fila, decimal pvp)
        {
            decimal conDescuento = pvp * (1M - fila.Descuento);
            return fila.Precio.HasValue && fila.Precio.Value > 0M && fila.Precio.Value < conDescuento ? fila.Precio.Value : conDescuento;
        }

        private static string DescribirFila(FilaExistenteCampana f)
        {
            string que = f.Precio.HasValue && f.Precio.Value > 0M
                ? $"un precio especial de {f.Precio.Value.ToString("0.00", CultureInfo.GetCultureInfo("es-ES"))} €" + (f.Descuento > 0M ? $" con {Porcentaje(f.Descuento)}" : string.Empty)
                : $"un {Porcentaje(f.Descuento)}";
            string deCampana = string.IsNullOrWhiteSpace(f.Campana) ? string.Empty : $" de la campaña «{f.Campana.Trim()}»";
            string fechas = f.FechaDesde.HasValue || f.FechaHasta.HasValue
                ? $" ({(f.FechaDesde.HasValue ? f.FechaDesde.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "siempre")} - {(f.FechaHasta.HasValue ? f.FechaHasta.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "sin fin")})"
                : " (sin fechas)";
            return $"{que}{deCampana} (fila nº {f.Id}){fechas}";
        }

        internal static string Porcentaje(decimal tantoPorUno) =>
            (tantoPorUno * 100M).ToString("0.##", CultureInfo.GetCultureInfo("es-ES")) + " %";

        /// <summary>Totales por familia (en el orden en que aparecen) y el total general.</summary>
        public static (List<TotalesCampanaBloqueDTO> PorFamilia, TotalesCampanaBloqueDTO Total) Totales(IEnumerable<LineaCampanaBloqueDTO> lineas)
        {
            List<LineaCampanaBloqueDTO> todas = (lineas ?? Enumerable.Empty<LineaCampanaBloqueDTO>()).ToList();
            List<TotalesCampanaBloqueDTO> porFamilia = todas
                .GroupBy(l => l.Familia ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => Sumar(g.Key == string.Empty ? "(sin ficha)" : g.Key, g))
                .ToList();
            return (porFamilia, Sumar(null, todas));
        }

        private static TotalesCampanaBloqueDTO Sumar(string familia, IEnumerable<LineaCampanaBloqueDTO> lineas)
        {
            List<LineaCampanaBloqueDTO> l = lineas.ToList();
            return new TotalesCampanaBloqueDTO
            {
                Familia = familia,
                Productos = l.Count,
                Entran = l.Count(x => x.Estado == EstadoLineaCampanaBloque.Entra),
                YaEstaban = l.Count(x => x.Estado == EstadoLineaCampanaBloque.YaEstaba),
                Fuera = l.Count(x => x.Estado == EstadoLineaCampanaBloque.Fuera),
                FueraPorMotivo = l.Where(x => x.Estado == EstadoLineaCampanaBloque.Fuera)
                    .GroupBy(x => x.Motivo).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                FichasDesbloqueadas = l.Count(x => x.Estado != EstadoLineaCampanaBloque.Fuera && x.DesbloquearFicha),
                Solapes = l.Count(x => x.Solape),
                CondicionContado = l.Count(x => x.Estado != EstadoLineaCampanaBloque.Fuera && x.CondicionContado)
            };
        }

        /// <summary>Hasta <paramref name="porFamilia"/> líneas de cada familia: primero las que entran.</summary>
        public static List<LineaCampanaBloqueDTO> Muestra(IEnumerable<LineaCampanaBloqueDTO> lineas, int porFamilia) =>
            (lineas ?? Enumerable.Empty<LineaCampanaBloqueDTO>())
                .GroupBy(l => l.Familia ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .SelectMany(g => g.OrderBy(l => l.Estado == EstadoLineaCampanaBloque.Entra ? 0 : 1).ThenBy(l => l.Producto, StringComparer.Ordinal).Take(porFamilia))
                .ToList();

        // ------------------------------------------------------------------

        /// <summary>Minúsculas, sin tildes y con todo lo que no es letra o cifra como espacio (un solo espacio).</summary>
        public static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }
            string descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                UnicodeCategory categoria = CharUnicodeInfo.GetUnicodeCategory(c);
                if (categoria == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                _ = sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return string.Join(" ", sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>El primer texto excluido que aparece en el nombre como palabra(s) entera(s), o null.</summary>
        public static string TextoExcluido(IEnumerable<string> textos, string nombre)
        {
            string enNombre = " " + NormalizarTexto(nombre) + " ";
            foreach (string texto in textos ?? Enumerable.Empty<string>())
            {
                string buscado = NormalizarTexto(texto);
                if (buscado.Length > 0 && enNombre.Contains(" " + buscado + " "))
                {
                    return texto.Trim();
                }
            }
            return null;
        }

        /// <summary>«OUT» excluye el subgrupo OUT de cualquier grupo; «COS/OUT», solo el de COS.</summary>
        public static bool SubgrupoExcluido(IEnumerable<string> subgrupos, string grupo, string subGrupo)
        {
            string g = grupo?.Trim() ?? string.Empty;
            string s = subGrupo?.Trim() ?? string.Empty;
            if (s.Length == 0)
            {
                return false;
            }
            foreach (string entrada in subgrupos ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(entrada))
                {
                    continue;
                }
                string[] partes = entrada.Trim().Split('/');
                if (partes.Length == 1 && Igual(partes[0], s))
                {
                    return true;
                }
                if (partes.Length == 2 && Igual(partes[0], g) && Igual(partes[1], s))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Igual(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static HashSet<string> Conjunto(IEnumerable<string> valores) =>
            new HashSet<string>((valores ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Si las filas de una campaña pueden estar AHORA en el mensaje de la tienda (y por tanto, al borrarlas o cerrarlas, hay
        /// que republicar): ya ha empezado y no acabó hace más de <paramref name="diasVentana"/> días (lo que acabó hace poco
        /// puede no haberlo retirado todavía el job <c>vigencia-campanas</c>). Una campaña que aún no ha empezado no está en la
        /// tienda: crearla o borrarla no cambia ningún mensaje.
        /// </summary>
        public static bool PuedeEstarEnLaTienda(DateTime? fechaDesde, DateTime? fechaHasta, DateTime hoy, int diasVentana)
        {
            DateTime dia = hoy.Date;
            return (fechaDesde == null || fechaDesde.Value.Date <= dia)
                && (fechaHasta == null || fechaHasta.Value.Date >= dia.AddDays(-Math.Max(0, diasVentana)));
        }
    }
}
