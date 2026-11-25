using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inmovilizados
{
    /// <summary>Un inmovilizado (tabla Inmovilizados, en el EDMX), tal como lo dan GET api/Inmovilizados y el 409/404 de «es un inmovilizado».</summary>
    public class InmovilizadoDTO
    {
        public string Empresa { get; set; }
        /// <summary>El número sin relleno («1823»).</summary>
        public string Numero { get; set; }
        /// <summary>La nomenclatura de la casa: «I/1823» (<see cref="CodigoInmovilizado"/>).</summary>
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public string Grupo { get; set; }
        /// <summary>ALG, REI, ALC o VAR.</summary>
        public string Delegacion { get; set; }
        /// <summary>0 Normal, -1 Anulado.</summary>
        public short Estado { get; set; }
        public string EstadoDescripcion { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    /// <summary>
    /// El cuerpo de la respuesta cuando lo leído donde se busca un producto es la etiqueta de un inmovilizado («I/1823»):
    /// <c>{ Codigo: "ES_INMOVILIZADO", Message, Inmovilizado, Productos: [] }</c>. <c>Productos</c> va siempre vacía para que los
    /// clientes que leen la lista de candidatos de un 409 no se encuentren un null.
    /// </summary>
    public class EsInmovilizadoDTO
    {
        public const string CODIGO = "ES_INMOVILIZADO";

        public string Codigo { get; set; } = CODIGO;
        public string Message { get; set; }
        /// <summary>Null si tiene formato de inmovilizado pero no existe.</summary>
        public InmovilizadoDTO Inmovilizado { get; set; }
        public List<object> Productos { get; set; } = new List<object>();
    }

    public interface IRepositorioInmovilizados
    {
        /// <summary>
        /// Los inmovilizados de la empresa, los más nuevos primero, como mucho <paramref name="maximo"/>. <paramref name="filtro"/>
        /// busca en el número (también escrito «I/1823», entonces ese número exacto) y en la descripción.
        /// </summary>
        Task<List<InmovilizadoDTO>> Buscar(string empresa, string filtro, string delegacion, bool soloVivos, int maximo);

        /// <summary>Los que existen de esos números (sin relleno), estén como estén.</summary>
        Task<List<InmovilizadoDTO>> Leer(string empresa, IEnumerable<string> numeros);
    }

    /// <summary>La tabla Inmovilizados por EF (está en el EDMX), solo lectura, con un contexto propio por consulta.</summary>
    public class RepositorioInmovilizadosEf : IRepositorioInmovilizados
    {
        private readonly Func<NVEntities> crearContexto;

        public RepositorioInmovilizadosEf() : this(() => new NVEntities())
        {
        }

        public RepositorioInmovilizadosEf(Func<NVEntities> crearContexto)
        {
            this.crearContexto = crearContexto ?? throw new ArgumentNullException(nameof(crearContexto));
        }

        public async Task<List<InmovilizadoDTO>> Buscar(string empresa, string filtro, string delegacion, bool soloVivos, int maximo)
        {
            using (NVEntities db = Contexto())
            {
                IQueryable<Inmovilizado> consulta = db.Inmovilizados.AsNoTracking().Where(i => i.Empresa == empresa);
                if (soloVivos)
                {
                    consulta = consulta.Where(i => i.Estado >= 0);
                }
                if (!string.IsNullOrWhiteSpace(delegacion))
                {
                    string d = delegacion.Trim();
                    consulta = consulta.Where(i => i.Delegación == d);
                }
                string numero = CodigoInmovilizado.Leer(filtro);
                if (numero != null)
                {
                    consulta = consulta.Where(i => i.Número.Trim() == numero);
                }
                else if (!string.IsNullOrWhiteSpace(filtro))
                {
                    string f = filtro.Trim();
                    consulta = consulta.Where(i => i.Número.Contains(f) || i.Descripción.Contains(f));
                }
                List<Inmovilizado> filas = await consulta
                    .OrderByDescending(i => i.FechaCreación).ThenByDescending(i => i.Número)
                    .Take(maximo)
                    .ToListAsync().ConfigureAwait(false);
                return filas.Select(ReglasInmovilizados.ADto).ToList();
            }
        }

        public async Task<List<InmovilizadoDTO>> Leer(string empresa, IEnumerable<string> numeros)
        {
            List<string> lista = (numeros ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct().ToList();
            if (lista.Count == 0)
            {
                return new List<InmovilizadoDTO>();
            }
            using (NVEntities db = Contexto())
            {
                List<Inmovilizado> filas = await db.Inmovilizados.AsNoTracking()
                    .Where(i => i.Empresa == empresa && lista.Contains(i.Número.Trim()))
                    .ToListAsync().ConfigureAwait(false);
                return filas.Select(ReglasInmovilizados.ADto).ToList();
            }
        }

        private NVEntities Contexto()
        {
            NVEntities db = crearContexto();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            return db;
        }
    }

    /// <summary>Las reglas (puras) de los inmovilizados: el DTO, el mensaje de «es un inmovilizado» y los permisos.</summary>
    public static class ReglasInmovilizados
    {
        /// <summary>GET api/Inmovilizados: como mucho.</summary>
        public const int MAXIMO_BUSQUEDA = 500;

        /// <summary>Quién imprime etiquetas de inmovilizado.</summary>
        public static readonly string[] GRUPOS_ETIQUETAS =
        {
            Constantes.GruposSeguridad.ADMINISTRACION,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.TIENDAS,
            Constantes.GruposSeguridad.INFORMATICA
        };

        public const string MENSAJE_SIN_PERMISO_ETIQUETAS =
            "Para imprimir etiquetas de inmovilizado hay que ser de Administración, Dirección, Almacén, Tiendas o Informática.";

        public static bool PuedeImprimirEtiquetas(IPrincipal usuario)
            => usuario != null && GRUPOS_ETIQUETAS.Any(g => usuario.IsInRoleSinDominio(g));

        public static InmovilizadoDTO ADto(Inmovilizado i)
        {
            string numero = CodigoInmovilizado.Normalizar(i?.Número);
            return new InmovilizadoDTO
            {
                Empresa = i?.Empresa?.Trim(),
                Numero = numero,
                Codigo = CodigoInmovilizado.Componer(numero),
                Descripcion = i?.Descripción?.Trim(),
                Grupo = i?.Grupo?.Trim(),
                Delegacion = i?.Delegación?.Trim(),
                Estado = i?.Estado ?? 0,
                EstadoDescripcion = CodigoInmovilizado.DescripcionEstado(i?.Estado ?? 0),
                FechaCreacion = i?.FechaCreación ?? DateTime.MinValue
            };
        }

        /// <summary>
        /// LA REGLA del mensaje cuando se lee una etiqueta de inmovilizado donde se busca un producto: «I/1823 es un inmovilizado
        /// (DEPILADOR …): no es stock, no se cuenta ni se prepara.»; si no existe, «I/9999 tiene formato de inmovilizado, pero no existe.».
        /// </summary>
        public static EsInmovilizadoDTO EsInmovilizado(string numero, InmovilizadoDTO inmovilizado)
        {
            string codigo = CodigoInmovilizado.Componer(numero ?? inmovilizado?.Numero);
            if (inmovilizado == null)
            {
                return new EsInmovilizadoDTO { Message = $"{codigo} tiene formato de inmovilizado, pero no existe." };
            }
            var detalle = new List<string>();
            if (!string.IsNullOrWhiteSpace(inmovilizado.Descripcion))
            {
                detalle.Add(inmovilizado.Descripcion.Trim());
            }
            if (inmovilizado.Estado < 0)
            {
                detalle.Add("anulado");
            }
            string entreParentesis = detalle.Count == 0 ? string.Empty : $" ({string.Join(", ", detalle)})";
            return new EsInmovilizadoDTO
            {
                Message = $"{codigo} es un inmovilizado{entreParentesis}: no es stock, no se cuenta ni se prepara.",
                Inmovilizado = inmovilizado
            };
        }

        /// <summary>
        /// Si lo leído es una etiqueta de inmovilizado («I/1823»), lo que hay que contestar (exista o no); si no, null y se sigue
        /// buscando el producto como siempre. Solo lee la BD cuando tiene el formato.
        /// </summary>
        public static async Task<EsInmovilizadoDTO> Reconocer(IRepositorioInmovilizados repositorio, string empresa, string leido)
        {
            string numero = CodigoInmovilizado.Leer(leido);
            if (numero == null || repositorio == null)
            {
                return null;
            }
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            InmovilizadoDTO encontrado = (await repositorio.Leer(empresaLimpia, new[] { numero }).ConfigureAwait(false) ?? new List<InmovilizadoDTO>())
                .FirstOrDefault(i => string.Equals(i?.Numero, numero, StringComparison.OrdinalIgnoreCase));
            return EsInmovilizado(numero, encontrado);
        }
    }
}
