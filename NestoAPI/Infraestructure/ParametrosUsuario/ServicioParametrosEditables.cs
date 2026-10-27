using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ParametrosUsuario
{
    public class OpcionParametroDTO
    {
        public string Valor { get; set; }
        public string Descripcion { get; set; }
    }

    public class ParametroEditableDTO
    {
        public string Clave { get; set; }
        public string Descripcion { get; set; }
        public string ValorActual { get; set; }
        /// <summary>Valor "titular" del usuario: el que se restaura cuando el cambio era
        /// temporal (p. ej. AMZ para quien factura FBA y cambia a ALG unos días). Null si el
        /// parámetro no gestiona titular o aún no se ha capturado.</summary>
        public string ValorTitular { get; set; }
        public List<OpcionParametroDTO> Opciones { get; set; }
        /// <summary>Nesto#490: el cambio no se nota hasta reiniciar Nesto (la ventana de Parámetros lo avisa).</summary>
        public bool RequiereReiniciar { get; set; }
    }

    public class CambioParametroRequest
    {
        public string Empresa { get; set; }
        public string Clave { get; set; }
        public string Valor { get; set; }
    }

    /// <summary>
    /// Parámetros de usuario que el PROPIO usuario puede cambiarse desde Nesto (caso real
    /// 20/08/26: el usuario de Tienda Online que factura los FBA con almacén AMZ necesita
    /// pasar a ALG los días que cubre las rutas por vacaciones, y volver). El catálogo de qué
    /// clave es editable, por qué grupo y con qué valores vive AQUÍ, server-side y declarativo:
    /// añadir un parámetro editable = una entrada en el catálogo, sin tocar el cliente (la
    /// ventana de Nesto pinta lo que este servicio declare). La validación del POST nunca
    /// confía en el cliente: grupo, clave y valor se comprueban contra el catálogo.
    /// </summary>
    public class ServicioParametrosEditables
    {
        internal const string USUARIO_DEFECTO = "(defecto)";

        internal class DefinicionParametroEditable
        {
            public string Clave { get; set; }
            public string Descripcion { get; set; }
            /// <summary>Grupos de seguridad cuyos miembros pueden editar la clave (basta uno).</summary>
            public string[] Grupos { get; set; }
            /// <summary>Nesto#490: la puede cambiar cualquier usuario (no se miran los grupos).</summary>
            public bool ParaTodos { get; set; }
            /// <summary>Nesto#490: el cambio no se nota hasta reiniciar Nesto (se lee al arrancar).</summary>
            public bool RequiereReiniciar { get; set; }
            /// <summary>Nesto#490: si el usuario no tiene fila propia, se le enseña el valor de la fila "(defecto)",
            /// que es el que de verdad tiene (sin esto, el combo saldría vacío).</summary>
            public bool MostrarDefectoSiNoTieneValor { get; set; }
            /// <summary>Clave donde se guarda el valor TITULAR (null = sin gestión de titular).
            /// Se captura solo la primera vez que el usuario cambia el valor: el que tenía
            /// pasa a ser su titular, y el arranque de Nesto ofrece restaurarlo.</summary>
            public string ClaveTitular { get; set; }
            public Func<NVEntities, string, Task<List<OpcionParametroDTO>>> CargarOpciones { get; set; }
        }

        // CATÁLOGO. Los almacenes salen de la tabla Almacenes (activos), sin hard-coding:
        // si mañana hay un almacén nuevo, aparece solo en el combo.
        internal static readonly List<DefinicionParametroEditable> Catalogo = new List<DefinicionParametroEditable>
        {
            new DefinicionParametroEditable
            {
                Clave = "AlmacénPedidoVta",
                Descripcion = "Almacén de pedidos de venta",
                Grupos = new[] { Constantes.GruposSeguridad.TIENDA_ON_LINE },
                ClaveTitular = "AlmacénPedidoVtaTitular",
                CargarOpciones = CargarAlmacenes
            },
            // Nesto#490 (panel lateral, 27/10/26): cada usuario elige si usa el diseño nuevo (menú lateral) o la cinta.
            // Fila "(defecto)" = "0" (la cinta) en Scripts/Nesto490_NavegacionLateral.sql; en unas semanas pasará a "1".
            // Nesto lo lee al crear la ventana principal: hay que reiniciar para que se note.
            new DefinicionParametroEditable
            {
                Clave = CLAVE_NAVEGACION_LATERAL,
                Descripcion = "Diseño nuevo: menú lateral en lugar de la cinta (requiere reiniciar Nesto)",
                Grupos = new string[0],
                ParaTodos = true,
                RequiereReiniciar = true,
                MostrarDefectoSiNoTieneValor = true,
                CargarOpciones = (bd, empresa) => Task.FromResult(new List<OpcionParametroDTO>
                {
                    new OpcionParametroDTO { Valor = "0", Descripcion = "Cinta (el diseño de siempre)" },
                    new OpcionParametroDTO { Valor = "1", Descripcion = "Menú lateral (el diseño nuevo)" }
                })
            }
        };

        /// <summary>Nesto#490: la clave del parámetro del menú lateral (Parametros.Claves.NavegacionLateral en Nesto).</summary>
        internal const string CLAVE_NAVEGACION_LATERAL = "NavegacionLateral";

        /// <summary>Si el usuario puede cambiar el parámetro: todos, o los de alguno de sus grupos.</summary>
        internal static bool PuedeEditar(DefinicionParametroEditable definicion, IPrincipal user)
            => definicion.ParaTodos || (definicion.Grupos ?? new string[0]).Any(g => user.IsInRoleSinDominio(g));

        private readonly NVEntities db;
        private readonly IReadOnlyList<DefinicionParametroEditable> catalogo;

        public ServicioParametrosEditables(NVEntities db) : this(db, null)
        {
        }

        internal ServicioParametrosEditables(NVEntities db, List<DefinicionParametroEditable> catalogoOverride)
        {
            this.db = db;
            catalogo = catalogoOverride ?? Catalogo;
        }

        internal static async Task<List<OpcionParametroDTO>> CargarAlmacenes(NVEntities db, string empresa)
        {
            return (await db.Database.SqlQuery<OpcionParametroDTO>(
                "SELECT RTRIM(Número) AS Valor, RTRIM(Descripción) AS Descripcion " +
                "FROM Almacenes WHERE Empresa = @p0 AND Estado >= 0 ORDER BY Número",
                new SqlParameter("@p0", empresa)).ToListAsync().ConfigureAwait(false));
        }

        /// <summary>Usuario del parámetro: sin dominio, como guarda la tabla ParametrosUsuario.</summary>
        internal static string UsuarioSinDominio(IPrincipal user)
        {
            string nombre = user?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }
            return nombre.Substring(nombre.IndexOf("\\") + 1).Trim();
        }

        public async Task<List<ParametroEditableDTO>> LeerEditables(IPrincipal user, string empresa)
        {
            string usuario = UsuarioSinDominio(user);
            var resultado = new List<ParametroEditableDTO>();
            if (usuario == null)
            {
                return resultado;
            }
            foreach (DefinicionParametroEditable definicion in catalogo)
            {
                if (!PuedeEditar(definicion, user))
                {
                    continue;
                }
                string valorActual = await LeerValor(empresa, usuario, definicion.Clave).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(valorActual) && definicion.MostrarDefectoSiNoTieneValor)
                {
                    valorActual = await LeerValor(empresa, USUARIO_DEFECTO, definicion.Clave).ConfigureAwait(false);
                }
                resultado.Add(new ParametroEditableDTO
                {
                    Clave = definicion.Clave,
                    Descripcion = definicion.Descripcion,
                    RequiereReiniciar = definicion.RequiereReiniciar,
                    ValorActual = valorActual,
                    ValorTitular = definicion.ClaveTitular == null
                        ? null
                        : await LeerValor(empresa, usuario, definicion.ClaveTitular).ConfigureAwait(false),
                    Opciones = await definicion.CargarOpciones(db, empresa).ConfigureAwait(false)
                });
            }
            return resultado;
        }

        /// <summary>
        /// Cambia el parámetro del PROPIO usuario autenticado, validando contra el catálogo.
        /// La primera vez que cambia el valor se captura el TITULAR (el valor que tenía), para
        /// que el arranque de Nesto pueda ofrecer restaurarlo. Lanza InvalidOperationException
        /// con el motivo si algo no está permitido (el controller lo devuelve como BadRequest).
        /// </summary>
        public async Task<ParametroEditableDTO> Cambiar(IPrincipal user, CambioParametroRequest peticion)
        {
            string usuario = UsuarioSinDominio(user);
            if (usuario == null)
            {
                throw new InvalidOperationException("No se ha podido identificar al usuario autenticado.");
            }
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Empresa)
                || string.IsNullOrWhiteSpace(peticion.Clave) || string.IsNullOrWhiteSpace(peticion.Valor))
            {
                throw new InvalidOperationException("Hay que indicar empresa, clave y valor.");
            }
            string empresa = peticion.Empresa.Trim();
            string clave = peticion.Clave.Trim();
            string valor = peticion.Valor.Trim();

            DefinicionParametroEditable definicion = catalogo.FirstOrDefault(d => d.Clave == clave);
            if (definicion == null)
            {
                throw new InvalidOperationException($"El parámetro '{clave}' no se puede modificar desde aquí.");
            }
            if (!PuedeEditar(definicion, user))
            {
                throw new InvalidOperationException($"Su usuario no tiene permiso para cambiar '{definicion.Descripcion}' " +
                    $"(hace falta pertenecer a: {string.Join(", ", definicion.Grupos)}).");
            }
            List<OpcionParametroDTO> opciones = await definicion.CargarOpciones(db, empresa).ConfigureAwait(false);
            if (!opciones.Any(o => o.Valor == valor))
            {
                throw new InvalidOperationException($"El valor '{valor}' no es válido para '{definicion.Descripcion}'. " +
                    $"Valores admitidos: {string.Join(", ", opciones.Select(o => o.Valor))}.");
            }

            string valorAnterior = await LeerValor(empresa, usuario, clave).ConfigureAwait(false);

            // TITULAR: se captura la primera vez que el usuario cambia el valor — lo que tenía
            // hasta hoy es su almacén "de verdad", y el arranque de Nesto ofrecerá restaurarlo
            // (mitiga el olvido: cambiar a ALG para las rutas y dejárselo puesto para siempre).
            string valorTitular = null;
            if (definicion.ClaveTitular != null)
            {
                valorTitular = await LeerValor(empresa, usuario, definicion.ClaveTitular).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(valorTitular) && !string.IsNullOrWhiteSpace(valorAnterior)
                    && valorAnterior != valor)
                {
                    await Escribir(empresa, usuario, definicion.ClaveTitular, valorAnterior).ConfigureAwait(false);
                    valorTitular = valorAnterior;
                }
            }

            if (valorAnterior != valor)
            {
                await Escribir(empresa, usuario, clave, valor).ConfigureAwait(false);
                _ = db.Modificaciones.Add(new Modificacion
                {
                    Tabla = "ParametrosUsuario",
                    Anterior = $"{usuario} {clave}={valorAnterior}",
                    Nuevo = $"{clave}={valor} (cambiado por el propio usuario desde Nesto)",
                    Usuario = usuario
                });
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }

            return new ParametroEditableDTO
            {
                Clave = clave,
                Descripcion = definicion.Descripcion,
                ValorActual = valor,
                ValorTitular = valorTitular,
                Opciones = opciones,
                RequiereReiniciar = definicion.RequiereReiniciar
            };
        }

        private async Task<string> LeerValor(string empresa, string usuario, string clave)
        {
            ParametroUsuario parametro = await db.ParametrosUsuario
                .FirstOrDefaultAsync(p => p.Empresa == empresa && p.Usuario.Trim() == usuario && p.Clave.Trim() == clave)
                .ConfigureAwait(false);
            return parametro?.Valor?.Trim();
        }

        private async Task Escribir(string empresa, string usuario, string clave, string valor)
        {
            ParametroUsuario parametro = await db.ParametrosUsuario
                .FirstOrDefaultAsync(p => p.Empresa == empresa && p.Usuario.Trim() == usuario && p.Clave.Trim() == clave)
                .ConfigureAwait(false);
            if (parametro == null)
            {
                _ = db.ParametrosUsuario.Add(new ParametroUsuario
                {
                    Empresa = empresa,
                    Usuario = usuario,
                    Clave = clave,
                    Valor = valor,
                    Usuario2 = usuario,
                    Fecha_Modificación = DateTime.Now
                });
            }
            else
            {
                parametro.Valor = valor;
                parametro.Usuario2 = usuario;
                parametro.Fecha_Modificación = DateTime.Now;
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
