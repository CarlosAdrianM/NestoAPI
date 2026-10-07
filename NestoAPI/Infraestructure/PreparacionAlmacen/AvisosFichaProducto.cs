using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Ariadna#8: los datos de la ficha de los que el mozo puede decir que están mal.</summary>
    public static class CamposFicha
    {
        public const string FOTO = "Foto";
        public const string PRECIO = "Precio";
        public const string NOMBRE = "Nombre";
        public const string FAMILIA = "Familia";
        public const string SUBGRUPO = "Subgrupo";
        /// <summary>Tamaño y unidad de medida.</summary>
        public const string TAMANO = "Tamano";
        public const string CODIGO_BARRAS = "CodigoBarras";
        /// <summary>Cualquier otra cosa (va en el comentario). No se puede detectar solo cuándo se corrige.</summary>
        public const string OTRO = "Otro";

        public static readonly IReadOnlyList<string> TODOS = new[] { FOTO, PRECIO, NOMBRE, FAMILIA, SUBGRUPO, TAMANO, CODIGO_BARRAS, OTRO };

        /// <summary>El campo tal como se guarda, o null si no es ninguno de los conocidos.</summary>
        public static string Normalizar(string campo)
            => TODOS.FirstOrDefault(c => string.Equals(c, campo?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static string Texto(string campo)
        {
            switch (campo)
            {
                case FOTO: return "la foto";
                case PRECIO: return "el precio";
                case NOMBRE: return "el nombre";
                case FAMILIA: return "la familia";
                case SUBGRUPO: return "el subgrupo";
                case TAMANO: return "el tamaño";
                case CODIGO_BARRAS: return "el código de barras";
                default: return "otro dato";
            }
        }

        public static string Textos(IEnumerable<string> campos)
        {
            List<string> textos = campos.Select(Texto).ToList();
            return textos.Count <= 1 ? textos.FirstOrDefault() ?? string.Empty
                : string.Join(", ", textos.Take(textos.Count - 1)) + " y " + textos.Last();
        }
    }

    /// <summary>Ariadna#8 (Carlos, 03/10/26): la foto la arregla Tienda online; todo lo demás, Compras.</summary>
    public static class DestinosAvisoFicha
    {
        public const string TIENDA_ONLINE = "TiendaOnline";
        public const string COMPRAS = "Compras";

        public static string De(string campo) => campo == CamposFicha.FOTO ? TIENDA_ONLINE : COMPRAS;

        public static string Nombre(string destino) => destino == TIENDA_ONLINE ? "Tienda online" : "Compras";

        public static string Correo(string destino) => destino == TIENDA_ONLINE ? Constantes.Correos.TIENDA_ONLINE : Constantes.Correos.COMPRAS;

        public static string Grupo(string destino) => destino == TIENDA_ONLINE ? Constantes.GruposSeguridad.TIENDA_ON_LINE : Constantes.GruposSeguridad.COMPRAS;
    }

    public static class EstadosAvisoFicha
    {
        public const string ABIERTO = "Abierto";
        public const string CAMBIADO = "Cambiado";
        public const string ESTABA_BIEN = "EstabaBien";

        /// <summary>«Cambiado» o «EstabaBien» (también «Estaba bien»); null si no es ninguno.</summary>
        public static string Cierre(string resultado)
        {
            string limpio = resultado?.Replace(" ", string.Empty).Trim();
            return string.Equals(limpio, CAMBIADO, StringComparison.OrdinalIgnoreCase) ? CAMBIADO
                : string.Equals(limpio, ESTABA_BIEN, StringComparison.OrdinalIgnoreCase) ? ESTABA_BIEN
                : null;
        }

        public static string Texto(string estado) => estado == CAMBIADO ? "cambiado" : estado == ESTABA_BIEN ? "estaba bien" : "abierto";
    }

    /// <summary>Lo que dice ahora la ficha de un producto (y la foto de la tienda, si se ha preguntado).</summary>
    public class DatosFichaActual
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        public string CodigoBarras { get; set; }
        public decimal? Precio { get; set; }
        public string UrlFoto { get; set; }

        /// <summary>El valor de un campo como texto, para guardarlo y compararlo después. Null en «Otro» (no se compara).</summary>
        public string Valor(string campo)
        {
            switch (campo)
            {
                case CamposFicha.FOTO: return UrlFoto?.Trim() ?? string.Empty;
                case CamposFicha.PRECIO: return Precio?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
                case CamposFicha.NOMBRE: return Nombre?.Trim() ?? string.Empty;
                case CamposFicha.FAMILIA: return Familia?.Trim() ?? string.Empty;
                case CamposFicha.SUBGRUPO: return Subgrupo?.Trim() ?? string.Empty;
                case CamposFicha.TAMANO: return $"{Tamano} {UnidadMedida?.Trim()}".Trim();
                case CamposFicha.CODIGO_BARRAS: return CodigoBarras?.Trim() ?? string.Empty;
                default: return null;
            }
        }
    }

    /// <summary>Un aviso de dato mal: uno abierto como mucho por producto y equipo (los siguientes se suman).</summary>
    public class AvisoFicha
    {
        public int Id { get; set; }
        /// <summary>Lo que lleva el enlace del correo para cerrarlo sin usuario: no se puede adivinar.</summary>
        public Guid Clave { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string Destino { get; set; }
        public List<string> Campos { get; set; } = new List<string>();
        public string Comentarios { get; set; }
        public string CodigoLeido { get; set; }
        /// <summary>NestoAPI#604: true = leído con el escáner, false = tecleado, null = no se sabe.</summary>
        public bool? CodigoLeidoConEscaner { get; set; }
        /// <summary>NestoAPI#604: de quién es el código leído, para decírselo al equipo. No se guarda: se mira al avisar.</summary>
        public InfoCodigoLeido InfoCodigo { get; set; }
        public string UrlFoto { get; set; }
        /// <summary>El valor de cada campo marcado cuando se avisó: si cambia, el aviso se cierra solo.</summary>
        public Dictionary<string, string> Huella { get; set; } = new Dictionary<string, string>();
        /// <summary>Los mozos que han avisado (a todos les llega la respuesta).</summary>
        public List<string> Informantes { get; set; } = new List<string>();
        public string Dispositivo { get; set; }
        public int Veces { get; set; } = 1;
        public string Estado { get; set; } = EstadosAvisoFicha.ABIERTO;
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaCierre { get; set; }
        public string CerradoPor { get; set; }
    }

    /// <summary>Un producto activo que lleva en la ficha un código de barras.</summary>
    public class ProductoConCodigo
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>
    /// NestoAPI#604 (caso 32565/32564 y 40510, 07/10/26): lo que se sabe del código que ha mandado el mozo. Es el de
    /// la ficha (el envase no se lee con el escáner), el de otro producto (en el hueco hay otra cosa) o el de ninguno.
    /// </summary>
    public class InfoCodigoLeido
    {
        public bool EsElDeLaFicha { get; set; }
        /// <summary>NestoAPI#605: es un código alternativo activo del propio producto (ProductosCodigosBarras): está bien.</summary>
        public bool EsCodigoAlternativo { get; set; }
        /// <summary>El otro producto activo que lleva ese código; null si no es de ninguno (o es el de la ficha).</summary>
        public ProductoConCodigo OtroProducto { get; set; }
        /// <summary>Si es el de la ficha: hoy se ha recogido a mano (MANUAL en la fase PICK) ese producto.</summary>
        public bool RecogidoAManoHoy { get; set; }
        /// <summary>Si es el de la ficha: hoy se ha empaquetado a mano (MANUAL en la fase PACK) ese producto.</summary>
        public bool EmpaquetadoAManoHoy { get; set; }
        /// <summary>El mozo ha avisado igual después de que Ariadna le dijera qué comprobar.</summary>
        public bool ConfirmadoTrasAviso { get; set; }
    }

    public interface ITransaccionAvisosFicha
    {
        /// <summary>El aviso abierto de ese producto y equipo, bloqueado hasta el final de la transacción.</summary>
        Task<AvisoFicha> LeerAbierto(string empresa, string producto, string destino);
        /// <summary>Lo guarda y le pone Id y Clave.</summary>
        Task<AvisoFicha> Insertar(AvisoFicha aviso);
        Task Actualizar(AvisoFicha aviso);
    }

    public interface IRepositorioAvisosFicha
    {
        /// <summary>Null si el producto no existe.</summary>
        Task<DatosFichaActual> LeerFicha(string empresa, string producto);
        Task<T> EnTransaccion<T>(Func<ITransaccionAvisosFicha, Task<T>> trabajo);
        /// <summary>NestoAPI#604: el producto activo (que no sea <paramref name="salvo"/>) con ese código; null si no hay.</summary>
        Task<ProductoConCodigo> LeerProductoConCodigo(string empresa, string codigo, string salvo);
        /// <summary>NestoAPI#605: el código es uno de los códigos activos del producto (ProductosCodigosBarras).</summary>
        Task<bool> EsCodigoDelProducto(string empresa, string producto, string codigo);
        /// <summary>NestoAPI#604: las fases (PICK, PACK) en las que hoy se ha tocado a mano (MANUAL) el producto.</summary>
        Task<List<string>> LeerFasesAManoHoy(string empresa, string producto);
        Task<List<AvisoFicha>> LeerAbiertos();
        Task<AvisoFicha> LeerPorId(int id);
        Task<AvisoFicha> LeerPorClave(Guid clave);
        /// <returns>False si ya estaba cerrado (o no existe): no se toca.</returns>
        Task<bool> Cerrar(int id, string estado, string cerradoPor);
    }

    /// <summary>A quién se avisa: al equipo que lo arregla (correo y buzón de Nesto) y, al cerrarse, al mozo (buzón de Ariadna).</summary>
    public interface IAvisadorFichaProducto
    {
        /// <param name="camposNuevos">Lo que se acaba de marcar (en un aviso sumado, solo lo que no estaba).</param>
        Task AvisarEquipo(AvisoFicha aviso, DatosFichaActual ficha, IReadOnlyCollection<string> camposNuevos);
        Task AvisarMozos(AvisoFicha aviso);
    }

    public enum EstadoInformarDatoMal
    {
        Guardado,
        NoValido,
        /// <summary>NestoAPI#604: no se ha creado nada; el mozo tiene que comprobar el código y, si quiere, confirmar.</summary>
        Comprobar
    }

    public class ResultadoInformarDatoMal
    {
        public EstadoInformarDatoMal Estado { get; set; }
        public string Mensaje { get; set; }
        public List<int> Avisos { get; set; } = new List<int>();
    }

    public enum EstadoCerrarAvisoFicha
    {
        Cerrado,
        SinPermiso,
        YaCerrado,
        NoExiste,
        NoValido
    }

    public class ResultadoCerrarAvisoFicha
    {
        public EstadoCerrarAvisoFicha Estado { get; set; }
        public string Mensaje { get; set; }
    }

    public interface IServicioAvisosFicha
    {
        Task<ResultadoInformarDatoMal> Informar(string empresa, InformarDatoMalDTO peticion, string usuario);
        Task<ResultadoCerrarAvisoFicha> Cerrar(int id, string resultado, IPrincipal usuario);
        Task<ResultadoCerrarAvisoFicha> CerrarConEnlace(Guid clave, string resultado);
        /// <summary>Cierra como «Cambiado» los avisos cuyo dato ya no es el que había al avisar. Devuelve cuántos.</summary>
        Task<int> RevisarCambios();
    }

    /// <summary>
    /// Ariadna#8 (Carlos, 03/10/26): el mozo tiene el producto delante y es quien mejor ve si la ficha no cuadra. Lo que
    /// marca va a quien lo arregla (la foto, a Tienda online; lo demás, a Compras) por correo y buzón de Nesto. Un aviso
    /// abierto por producto y equipo: si otro mozo avisa de lo mismo, se suma. Se cierra a mano («Cambiado» / «Estaba
    /// bien») o solo, como «Cambiado», cuando el dato ya no es el que había; al cerrarse, el mozo lo ve en Ariadna.
    /// </summary>
    public class ServicioAvisosFicha : IServicioAvisosFicha
    {
        private const string ROL_ADMIN = "Admin";
        public const string CERRADO_SOLO = "Detectado al cambiar la ficha";

        private readonly IRepositorioAvisosFicha repositorio;
        private readonly IAvisadorFichaProducto avisador;
        private readonly IFotosProductoAlmacen fotos;

        public ServicioAvisosFicha(IRepositorioAvisosFicha repositorio, IAvisadorFichaProducto avisador, IFotosProductoAlmacen fotos)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.avisador = avisador ?? throw new ArgumentNullException(nameof(avisador));
            this.fotos = fotos ?? throw new ArgumentNullException(nameof(fotos));
        }

        public async Task<ResultadoInformarDatoMal> Informar(string empresa, InformarDatoMalDTO peticion, string usuario)
        {
            string producto = peticion?.Producto?.Trim();
            List<string> marcados = (peticion?.Campos ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            List<string> campos = marcados.Select(CamposFicha.Normalizar).Where(c => c != null).Distinct().ToList();
            if (string.IsNullOrEmpty(producto))
            {
                return NoValido("Falta el producto.");
            }
            if (campos.Count == 0 || campos.Count != marcados.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            {
                return NoValido("Marca qué está mal: " + string.Join(", ", CamposFicha.TODOS.Select(CamposFicha.Texto)) + ".");
            }
            DatosFichaActual ficha = await repositorio.LeerFicha(empresa, producto).ConfigureAwait(false);
            if (ficha == null)
            {
                return NoValido($"No existe el producto {producto}.");
            }
            if (campos.Contains(CamposFicha.FOTO))
            {
                ficha.UrlFoto = await fotos.UrlActual(producto).ConfigureAwait(false);
            }
            InfoCodigoLeido infoCodigo = await QueEsElCodigo(empresa, producto, ficha, Limpio(peticion.CodigoLeido)).ConfigureAwait(false);
            string comprobar = QueComprobar(infoCodigo, peticion.CodigoLeidoConEscaner, producto);
            if (comprobar != null)
            {
                if (!peticion.Confirmado)
                {
                    // NestoAPI#604: antes del correo, que el mozo mire el hueco o el envase
                    return new ResultadoInformarDatoMal { Estado = EstadoInformarDatoMal.Comprobar, Mensaje = comprobar };
                }
                infoCodigo.ConfirmadoTrasAviso = true;
            }

            string quien = usuario?.Trim() ?? string.Empty;
            var resultado = new ResultadoInformarDatoMal { Estado = EstadoInformarDatoMal.Guardado };
            var mensajes = new List<string>();
            foreach (IGrouping<string, string> grupo in campos.GroupBy(DestinosAvisoFicha.De).OrderBy(g => g.Key == DestinosAvisoFicha.TIENDA_ONLINE ? 0 : 1))
            {
                List<string> nuevos = null;
                AvisoFicha aviso = await repositorio.EnTransaccion(async tx =>
                {
                    AvisoFicha existente = await tx.LeerAbierto(empresa, producto, grupo.Key).ConfigureAwait(false);
                    if (existente == null)
                    {
                        nuevos = grupo.ToList();
                        return await tx.Insertar(Nuevo(empresa, producto, grupo.Key, nuevos, ficha, peticion, quien)).ConfigureAwait(false);
                    }
                    nuevos = grupo.Where(c => !existente.Campos.Contains(c)).ToList();
                    Sumar(existente, nuevos, ficha, peticion, quien);
                    await tx.Actualizar(existente).ConfigureAwait(false);
                    return existente;
                }).ConfigureAwait(false);

                if (infoCodigo != null)
                {
                    aviso.InfoCodigo = infoCodigo;
                }
                resultado.Avisos.Add(aviso.Id);
                string equipo = DestinosAvisoFicha.Nombre(grupo.Key);
                if (nuevos.Count > 0)
                {
                    await avisador.AvisarEquipo(aviso, ficha, nuevos).ConfigureAwait(false);
                    mensajes.Add(aviso.Veces > 1 ? $"Añadido al aviso que ya tenía {equipo}." : $"Avisado a {equipo}.");
                }
                else
                {
                    mensajes.Add($"{equipo} ya estaba avisado: se ha sumado tu aviso.");
                }
            }
            resultado.Mensaje = string.Join(" ", mensajes) + " Te llegará la respuesta al buzón.";
            return resultado;
        }

        /// <summary>
        /// NestoAPI#604: si el código es el de la ficha, el envase no se lee (y se mira si hoy se ha tocado a mano); si
        /// es el de otro producto, probablemente en el hueco hay ese otro. Null si no viene código.
        /// </summary>
        private async Task<InfoCodigoLeido> QueEsElCodigo(string empresa, string producto, DatosFichaActual ficha, string codigo)
        {
            if (codigo == null)
            {
                return null;
            }
            if (string.Equals(codigo, ficha.CodigoBarras?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                List<string> fases = await repositorio.LeerFasesAManoHoy(empresa, producto).ConfigureAwait(false) ?? new List<string>();
                return new InfoCodigoLeido
                {
                    EsElDeLaFicha = true,
                    RecogidoAManoHoy = fases.Any(f => string.Equals(f?.Trim(), "PICK", StringComparison.OrdinalIgnoreCase)),
                    EmpaquetadoAManoHoy = fases.Any(f => string.Equals(f?.Trim(), "PACK", StringComparison.OrdinalIgnoreCase))
                };
            }
            // NestoAPI#605: un código alternativo del propio producto no es «de otro producto»: es correcto
            if (await repositorio.EsCodigoDelProducto(empresa, producto, codigo).ConfigureAwait(false))
            {
                return new InfoCodigoLeido { EsCodigoAlternativo = true };
            }
            return new InfoCodigoLeido
            {
                OtroProducto = await repositorio.LeerProductoConCodigo(empresa, codigo, producto).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// NestoAPI#604 (Carlos, 07/10/26): lo que tiene que comprobar el mozo antes de que se avise a nadie, o null si el
        /// código no es de ninguna ficha (o no viene): eso sí es para Compras.
        /// </summary>
        internal static string QueComprobar(InfoCodigoLeido info, bool? conEscaner, string producto)
        {
            if (info == null)
            {
                return null;
            }
            if (info.OtroProducto != null)
            {
                return $"Ese código es del producto {info.OtroProducto.Producto} {info.OtroProducto.Nombre}. Comprueba el hueco: puede que esté ese producto en vez del {producto}. Si este envase es del {producto}, en Ariadna puedes añadirlo como código de este producto. Si aun así quieres avisar a Compras, vuelve a enviar.";
            }
            if (info.EsCodigoAlternativo)
            {
                return "Ese código ya es uno de los códigos de este producto: el código de barras está bien. Si aun así quieres avisar, vuelve a enviar.";
            }
            if (!info.EsElDeLaFicha)
            {
                return null;
            }
            return conEscaner == true
                ? "Ese es el código de la ficha y lo has leído con el escáner: el código de barras está bien. Si aun así quieres avisar, vuelve a enviar."
                : "Ese es el código de la ficha. Lee con el escáner el código del envase, o deja el cuadro vacío y haz una foto. Si aun así quieres avisar, vuelve a enviar.";
        }

        private static ResultadoInformarDatoMal NoValido(string mensaje)
            => new ResultadoInformarDatoMal { Estado = EstadoInformarDatoMal.NoValido, Mensaje = mensaje };

        private static AvisoFicha Nuevo(string empresa, string producto, string destino, List<string> campos, DatosFichaActual ficha,
            InformarDatoMalDTO peticion, string usuario)
        {
            var aviso = new AvisoFicha
            {
                Empresa = empresa,
                Producto = producto,
                Destino = destino,
                Campos = campos.ToList(),
                Comentarios = LineaComentario(usuario, peticion.Comentario),
                CodigoLeido = Limpio(peticion.CodigoLeido),
                CodigoLeidoConEscaner = Limpio(peticion.CodigoLeido) == null ? null : peticion.CodigoLeidoConEscaner,
                UrlFoto = campos.Contains(CamposFicha.FOTO) ? Limpio(peticion.UrlFoto) ?? ficha.UrlFoto : null,
                Informantes = new List<string> { usuario },
                Dispositivo = Limpio(peticion.Dispositivo),
                Veces = 1
            };
            Anotar(aviso.Huella, campos, ficha);
            return aviso;
        }

        private static void Sumar(AvisoFicha aviso, List<string> nuevos, DatosFichaActual ficha, InformarDatoMalDTO peticion, string usuario)
        {
            aviso.Campos.AddRange(nuevos);
            Anotar(aviso.Huella, nuevos, ficha);
            aviso.Veces++;
            if (!aviso.Informantes.Contains(usuario, StringComparer.OrdinalIgnoreCase))
            {
                aviso.Informantes.Add(usuario);
            }
            string linea = LineaComentario(usuario, peticion.Comentario);
            if (linea != null)
            {
                aviso.Comentarios = string.IsNullOrEmpty(aviso.Comentarios) ? linea : aviso.Comentarios + Environment.NewLine + linea;
            }
            if (Limpio(peticion.CodigoLeido) != null)
            {
                aviso.CodigoLeido = Limpio(peticion.CodigoLeido);
                aviso.CodigoLeidoConEscaner = peticion.CodigoLeidoConEscaner;
            }
            if (nuevos.Contains(CamposFicha.FOTO))
            {
                aviso.UrlFoto = Limpio(peticion.UrlFoto) ?? ficha.UrlFoto;
            }
        }

        // Lo que decía la ficha al avisar de cada campo que se puede comparar después
        private static void Anotar(Dictionary<string, string> huella, IEnumerable<string> campos, DatosFichaActual ficha)
        {
            foreach (string campo in campos)
            {
                // Si la tienda no ha contestado no se sabe qué foto había: ese aviso solo se cierra a mano
                if (campo == CamposFicha.FOTO && string.IsNullOrEmpty(ficha.UrlFoto))
                {
                    continue;
                }
                string valor = ficha.Valor(campo);
                if (valor != null)
                {
                    huella[campo] = valor;
                }
            }
        }

        private static string LineaComentario(string usuario, string comentario)
            => string.IsNullOrWhiteSpace(comentario) ? null : $"{usuario}: {comentario.Trim()}";

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        public async Task<ResultadoCerrarAvisoFicha> Cerrar(int id, string resultado, IPrincipal usuario)
        {
            string estado = EstadosAvisoFicha.Cierre(resultado);
            if (estado == null)
            {
                return new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.NoValido, Mensaje = "El resultado tiene que ser «Cambiado» o «EstabaBien»." };
            }
            AvisoFicha aviso = await repositorio.LeerPorId(id).ConfigureAwait(false);
            if (aviso == null)
            {
                return new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.NoExiste, Mensaje = $"No existe el aviso {id}." };
            }
            if (!PuedeCerrar(usuario, aviso.Destino))
            {
                return new ResultadoCerrarAvisoFicha
                {
                    Estado = EstadoCerrarAvisoFicha.SinPermiso,
                    Mensaje = $"Este aviso lo cierra {DestinosAvisoFicha.Nombre(aviso.Destino)} (o Dirección)."
                };
            }
            return await CerrarAviso(aviso, estado, usuario?.Identity?.Name).ConfigureAwait(false);
        }

        public async Task<ResultadoCerrarAvisoFicha> CerrarConEnlace(Guid clave, string resultado)
        {
            string estado = EstadosAvisoFicha.Cierre(resultado);
            AvisoFicha aviso = clave == Guid.Empty ? null : await repositorio.LeerPorClave(clave).ConfigureAwait(false);
            if (aviso == null)
            {
                return new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.NoExiste, Mensaje = "Este enlace no es de ningún aviso." };
            }
            if (estado == null)
            {
                return new ResultadoCerrarAvisoFicha { Estado = EstadoCerrarAvisoFicha.NoValido, Mensaje = "El enlace no dice si se ha cambiado o estaba bien." };
            }
            return await CerrarAviso(aviso, estado, $"Enlace del correo ({DestinosAvisoFicha.Correo(aviso.Destino)})").ConfigureAwait(false);
        }

        public async Task<int> RevisarCambios()
        {
            int cerrados = 0;
            var fichas = new Dictionary<string, DatosFichaActual>(StringComparer.OrdinalIgnoreCase);
            var fotosActuales = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (AvisoFicha aviso in await repositorio.LeerAbiertos().ConfigureAwait(false) ?? new List<AvisoFicha>())
            {
                if (aviso.Huella == null || aviso.Huella.Count == 0)
                {
                    continue; // Solo «Otro»: no hay nada que comparar
                }
                string clave = aviso.Empresa?.Trim() + "|" + aviso.Producto?.Trim();
                if (!fichas.TryGetValue(clave, out DatosFichaActual ficha))
                {
                    ficha = await repositorio.LeerFicha(aviso.Empresa, aviso.Producto).ConfigureAwait(false);
                    fichas[clave] = ficha;
                }
                if (ficha == null)
                {
                    continue;
                }
                if (aviso.Huella.ContainsKey(CamposFicha.FOTO))
                {
                    // Sin caché: la de 12 horas escondería la foto nueva
                    if (!fotosActuales.TryGetValue(clave, out string foto))
                    {
                        foto = await fotos.UrlActual(aviso.Producto).ConfigureAwait(false);
                        fotosActuales[clave] = foto;
                    }
                    ficha.UrlFoto = foto;
                }
                if (HaCambiado(aviso, ficha))
                {
                    ResultadoCerrarAvisoFicha cierre = await CerrarAviso(aviso, EstadosAvisoFicha.CAMBIADO, CERRADO_SOLO).ConfigureAwait(false);
                    if (cierre.Estado == EstadoCerrarAvisoFicha.Cerrado)
                    {
                        cerrados++;
                    }
                }
            }
            return cerrados;
        }

        private static bool HaCambiado(AvisoFicha aviso, DatosFichaActual ficha)
        {
            foreach (KeyValuePair<string, string> anotado in aviso.Huella)
            {
                // Sin respuesta de la tienda no se sabe la foto: no es «ha cambiado»
                if (anotado.Key == CamposFicha.FOTO && string.IsNullOrEmpty(ficha.UrlFoto))
                {
                    continue;
                }
                string ahora = ficha.Valor(anotado.Key);
                if (ahora != null && !string.Equals(ahora, anotado.Value ?? string.Empty, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private async Task<ResultadoCerrarAvisoFicha> CerrarAviso(AvisoFicha aviso, string estado, string cerradoPor)
        {
            if (!await repositorio.Cerrar(aviso.Id, estado, cerradoPor).ConfigureAwait(false))
            {
                return new ResultadoCerrarAvisoFicha
                {
                    Estado = EstadoCerrarAvisoFicha.YaCerrado,
                    Mensaje = $"El aviso del {aviso.Producto} ya estaba cerrado."
                };
            }
            aviso.Estado = estado;
            aviso.CerradoPor = cerradoPor;
            aviso.FechaCierre = DateTime.Now;
            if (estado == EstadosAvisoFicha.CAMBIADO && aviso.Campos.Contains(CamposFicha.FOTO))
            {
                // El mozo tiene que ver la foto nueva ya, no dentro de 12 horas
                fotos.Olvidar(aviso.Producto);
            }
            await avisador.AvisarMozos(aviso).ConfigureAwait(false);
            return new ResultadoCerrarAvisoFicha
            {
                Estado = EstadoCerrarAvisoFicha.Cerrado,
                Mensaje = $"Aviso del {aviso.Producto} cerrado: {EstadosAvisoFicha.Texto(estado)}. Se lo decimos al mozo que avisó."
            };
        }

        /// <summary>El equipo que lo arregla, Dirección o Admin.</summary>
        internal static bool PuedeCerrar(IPrincipal usuario, string destino)
        {
            return usuario != null && (usuario.IsInRoleSinDominio(ROL_ADMIN)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(DestinosAvisoFicha.Grupo(destino)));
        }
    }

    public class RepositorioAvisosFichaSql : IRepositorioAvisosFicha, ITransaccionAvisosFicha, IDisposable
    {
        internal const string SQL_FICHA = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, RTRIM(f.[Descripción]) AS Familia, RTRIM(s.[Descripción]) AS Subgrupo,
       p.[Tamaño] AS Tamano, RTRIM(p.UnidadMedida) AS UnidadMedida, RTRIM(p.CodBarras) AS CodigoBarras, CAST(p.PVP AS decimal(19, 4)) AS Precio
FROM Productos p
     LEFT JOIN Familias f ON f.Empresa = p.Empresa AND f.[Número] = p.Familia
     LEFT JOIN SubGruposProducto s ON s.Empresa = p.Empresa AND s.Grupo = p.Grupo AND s.[Número] = p.SubGrupo
WHERE p.Empresa = @empresa AND p.[Número] = @producto";

        internal const string SQL_AVISOS = @"
SELECT Id, Clave, RTRIM(Empresa) AS Empresa, RTRIM(Producto) AS Producto, Destino, Campos, Comentarios, CodigoLeido, CodigoLeidoConEscaner, UrlFoto, Huella,
       Informantes, Dispositivo, Veces, Estado, FechaCreacion, FechaCierre, CerradoPor
FROM dbo.AvisosFichaProducto";

        internal const string SQL_INSERTAR = @"
INSERT INTO dbo.AvisosFichaProducto (Empresa, Producto, Destino, Campos, Comentarios, CodigoLeido, CodigoLeidoConEscaner, UrlFoto, Huella, Informantes, Dispositivo, Veces)
OUTPUT INSERTED.Id, INSERTED.Clave, INSERTED.FechaCreacion
VALUES (@empresa, @producto, @destino, @campos, @comentarios, @codigoLeido, @conEscaner, @urlFoto, @huella, @informantes, @dispositivo, @veces)";

        internal const string SQL_ACTUALIZAR = @"
UPDATE dbo.AvisosFichaProducto
SET Campos = @campos, Comentarios = @comentarios, CodigoLeido = @codigoLeido, CodigoLeidoConEscaner = @conEscaner, UrlFoto = @urlFoto, Huella = @huella,
    Informantes = @informantes, Veces = @veces, FechaModificacion = GETDATE()
WHERE Id = @id AND Estado = 'Abierto'";

        internal const string SQL_CERRAR = @"
UPDATE dbo.AvisosFichaProducto SET Estado = @estado, CerradoPor = @cerradoPor, FechaCierre = GETDATE(), FechaModificacion = GETDATE()
WHERE Id = @id AND Estado = 'Abierto'";

        // NestoAPI#604: el producto activo (Estado >= 0) que lleva ese código en la ficha, salvo el del aviso.
        // NestoAPI#605: o como código activo en ProductosCodigosBarras (la ficha manda si están los dos).
        internal const string SQL_PRODUCTO_CON_CODIGO = @"
SELECT TOP 1 x.Producto, x.Nombre
FROM (SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, 0 AS Orden
      FROM Productos p
      WHERE p.Empresa = @empresa AND p.CodBarras = @codigo AND p.Estado >= 0 AND p.[Número] <> @producto
      UNION ALL
      SELECT RTRIM(p.[Número]), RTRIM(p.Nombre), 1
      FROM ProductosCodigosBarras c
           JOIN Productos p ON p.Empresa = c.Empresa AND p.[Número] = c.Producto
      WHERE c.Empresa = @empresa AND c.Codigo = @codigo AND c.Activo = 1 AND p.Estado >= 0 AND c.Producto <> @producto) x
ORDER BY x.Orden, x.Producto";

        // NestoAPI#605: el código es uno de los activos del producto
        internal const string SQL_ES_CODIGO_DEL_PRODUCTO = @"
SELECT COUNT(*) FROM ProductosCodigosBarras c
WHERE c.Empresa = @empresa AND c.Producto = @producto AND c.Codigo = @codigo AND c.Activo = 1";

        /// <summary>NestoAPI#605: ProductosCodigosBarras.Codigo es varchar(20).</summary>
        internal const int LONGITUD_CODIGO_TABLA = 20;

        // NestoAPI#604: va por IX_PreparacionEscaneos_Producto (Empresa, Producto, FechaEscaneo), del script #604
        internal const string SQL_FASES_A_MANO_HOY = @"
SELECT DISTINCT RTRIM(e.Fase) AS Fase
FROM PreparacionEscaneos e
WHERE e.Empresa = @empresa AND e.Producto = @producto AND e.Metodo = 'MANUAL'
  AND e.FechaEscaneo >= CAST(GETDATE() AS date)";

        /// <summary>Productos.CodBarras es char(13): un código más largo no puede ser de ninguna ficha.</summary>
        internal const int LONGITUD_CODIGO_BARRAS = 13;

        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioAvisosFichaSql(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioAvisosFichaSql ConContextoPropio() => new RepositorioAvisosFichaSql(new NVEntities()) { contextoPropio = true };

        public Task<DatosFichaActual> LeerFicha(string empresa, string producto)
            => db.Database.SqlQuery<DatosFichaActual>(SQL_FICHA, Char("@empresa", empresa, 3), Char("@producto", producto?.Trim(), 15)).FirstOrDefaultAsync();

        public async Task<ProductoConCodigo> LeerProductoConCodigo(string empresa, string codigo, string salvo)
        {
            string limpio = codigo?.Trim();
            if (string.IsNullOrEmpty(limpio) || limpio.Length > LONGITUD_CODIGO_TABLA)
            {
                return null;
            }
            // varchar: casa con el char(13) de la ficha y el varchar(20) de la tabla (un código de 14+ no casa con la ficha)
            return await db.Database.SqlQuery<ProductoConCodigo>(SQL_PRODUCTO_CON_CODIGO, Char("@empresa", empresa, 3),
                new SqlParameter("@codigo", System.Data.SqlDbType.VarChar, LONGITUD_CODIGO_TABLA) { Value = limpio },
                Char("@producto", salvo?.Trim(), 15)).FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public async Task<bool> EsCodigoDelProducto(string empresa, string producto, string codigo)
        {
            string limpio = codigo?.Trim();
            if (string.IsNullOrEmpty(limpio) || limpio.Length > LONGITUD_CODIGO_TABLA || string.IsNullOrWhiteSpace(producto))
            {
                return false;
            }
            int veces = await db.Database.SqlQuery<int>(SQL_ES_CODIGO_DEL_PRODUCTO, Char("@empresa", empresa, 3),
                Char("@producto", producto.Trim(), 15),
                new SqlParameter("@codigo", System.Data.SqlDbType.VarChar, LONGITUD_CODIGO_TABLA) { Value = limpio })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            return veces > 0;
        }

        public Task<List<string>> LeerFasesAManoHoy(string empresa, string producto)
            => db.Database.SqlQuery<string>(SQL_FASES_A_MANO_HOY, Char("@empresa", empresa, 3), Char("@producto", producto?.Trim(), 15)).ToListAsync();

        public async Task<T> EnTransaccion<T>(Func<ITransaccionAvisosFicha, Task<T>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    T resultado = await trabajo(this).ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch
                {
                    transaccion.Rollback();
                    throw;
                }
            }
        }

        public async Task<AvisoFicha> LeerAbierto(string empresa, string producto, string destino)
        {
            List<FilaAviso> filas = await db.Database.SqlQuery<FilaAviso>(
                SQL_AVISOS + " WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Producto = @producto AND Destino = @destino AND Estado = 'Abierto'",
                Char("@empresa", empresa, 3), Char("@producto", producto, 15), Texto("@destino", destino)).ToListAsync().ConfigureAwait(false);
            return filas.Select(f => f.Aviso()).FirstOrDefault();
        }

        public async Task<AvisoFicha> Insertar(AvisoFicha aviso)
        {
            FilaInsertada fila = await db.Database.SqlQuery<FilaInsertada>(SQL_INSERTAR,
                Char("@empresa", aviso.Empresa, 3), Char("@producto", aviso.Producto, 15), Texto("@destino", aviso.Destino),
                Texto("@campos", string.Join(",", aviso.Campos)), Texto("@comentarios", aviso.Comentarios), Texto("@codigoLeido", aviso.CodigoLeido),
                Bit("@conEscaner", aviso.CodigoLeidoConEscaner),
                Texto("@urlFoto", aviso.UrlFoto), Texto("@huella", JsonConvert.SerializeObject(aviso.Huella)),
                Texto("@informantes", string.Join(",", aviso.Informantes)), Texto("@dispositivo", aviso.Dispositivo), new SqlParameter("@veces", aviso.Veces))
                .SingleAsync().ConfigureAwait(false);
            aviso.Id = fila.Id;
            aviso.Clave = fila.Clave;
            aviso.FechaCreacion = fila.FechaCreacion;
            return aviso;
        }

        public Task Actualizar(AvisoFicha aviso)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_ACTUALIZAR,
                Texto("@campos", string.Join(",", aviso.Campos)), Texto("@comentarios", aviso.Comentarios), Texto("@codigoLeido", aviso.CodigoLeido), Bit("@conEscaner", aviso.CodigoLeidoConEscaner),
                Texto("@urlFoto", aviso.UrlFoto), Texto("@huella", JsonConvert.SerializeObject(aviso.Huella)),
                Texto("@informantes", string.Join(",", aviso.Informantes)), new SqlParameter("@veces", aviso.Veces), new SqlParameter("@id", aviso.Id));
        }

        public async Task<List<AvisoFicha>> LeerAbiertos()
            => (await db.Database.SqlQuery<FilaAviso>(SQL_AVISOS + " WHERE Estado = 'Abierto'").ToListAsync().ConfigureAwait(false))
                .Select(f => f.Aviso()).ToList();

        public async Task<AvisoFicha> LeerPorId(int id)
            => (await db.Database.SqlQuery<FilaAviso>(SQL_AVISOS + " WHERE Id = @id", new SqlParameter("@id", id)).ToListAsync().ConfigureAwait(false))
                .Select(f => f.Aviso()).FirstOrDefault();

        public async Task<AvisoFicha> LeerPorClave(Guid clave)
            => (await db.Database.SqlQuery<FilaAviso>(SQL_AVISOS + " WHERE Clave = @clave", new SqlParameter("@clave", clave)).ToListAsync().ConfigureAwait(false))
                .Select(f => f.Aviso()).FirstOrDefault();

        public async Task<bool> Cerrar(int id, string estado, string cerradoPor)
        {
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(cerradoPor);
            int filas = await db.Database.ExecuteSqlCommandAsync(SQL_CERRAR,
                Texto("@estado", estado), Texto("@cerradoPor", quien), new SqlParameter("@id", id)).ConfigureAwait(false);
            return filas > 0;
        }

        // Tipados como las columnas (char): con nvarchar SQL Server convertiría cada fila y no usaría el índice
        private static SqlParameter Char(string nombre, string valor, int longitud)
            => new SqlParameter(nombre, System.Data.SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter Texto(string nombre, string valor)
            => new SqlParameter(nombre, System.Data.SqlDbType.NVarChar, -1) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter Bit(string nombre, bool? valor)
            => new SqlParameter(nombre, System.Data.SqlDbType.Bit) { Value = valor.HasValue ? (object)valor.Value : DBNull.Value };

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }

        private class FilaInsertada
        {
            public int Id { get; set; }
            public Guid Clave { get; set; }
            public DateTime FechaCreacion { get; set; }
        }

        internal class FilaAviso
        {
            public int Id { get; set; }
            public Guid Clave { get; set; }
            public string Empresa { get; set; }
            public string Producto { get; set; }
            public string Destino { get; set; }
            public string Campos { get; set; }
            public string Comentarios { get; set; }
            public string CodigoLeido { get; set; }
            public bool? CodigoLeidoConEscaner { get; set; }
            public string UrlFoto { get; set; }
            public string Huella { get; set; }
            public string Informantes { get; set; }
            public string Dispositivo { get; set; }
            public int Veces { get; set; }
            public string Estado { get; set; }
            public DateTime FechaCreacion { get; set; }
            public DateTime? FechaCierre { get; set; }
            public string CerradoPor { get; set; }

            public AvisoFicha Aviso() => new AvisoFicha
            {
                Id = Id,
                Clave = Clave,
                Empresa = Empresa,
                Producto = Producto,
                Destino = Destino?.Trim(),
                Campos = Lista(Campos),
                Comentarios = Comentarios,
                CodigoLeido = CodigoLeido,
                CodigoLeidoConEscaner = CodigoLeidoConEscaner,
                UrlFoto = UrlFoto,
                Huella = string.IsNullOrWhiteSpace(Huella) ? new Dictionary<string, string>() : JsonConvert.DeserializeObject<Dictionary<string, string>>(Huella),
                Informantes = Lista(Informantes),
                Dispositivo = Dispositivo,
                Veces = Veces,
                Estado = Estado?.Trim(),
                FechaCreacion = FechaCreacion,
                FechaCierre = FechaCierre,
                CerradoPor = CerradoPor
            };

            private static List<string> Lista(string texto)
                => (texto ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList();
        }
    }

    /// <summary>
    /// Ariadna#8: el correo al equipo (con la foto y dos botones para cerrarlo: «Cambiado» y «Estaba bien», enlaces que
    /// no se pueden adivinar), el buzón de Nesto de cada miembro del grupo de Windows (como AvisadorCompras) y, al
    /// cerrarse, el buzón de Ariadna de cada mozo que avisó.
    /// </summary>
    public class AvisadorFichaProducto : IAvisadorFichaProducto
    {
        public const string TIPO_NOTIFICACION = "AvisoFichaProducto";
        public const string RUTA_ENLACE = "api/Almacen/AvisosFicha/Enlace/";
        /// <summary>NestoAPI#604: Ariadna le ha dicho qué comprobar y ha vuelto a enviar.</summary>
        public const string CONFIRMADO_TRAS_AVISO = "El mozo lo ha confirmado tras el aviso de Ariadna (se le pidió comprobar el hueco o el envase).";
        private const string DOMINIO = "NUEVAVISION\\";
        private const string URL_API = "https://api.nuevavision.es/";

        private readonly IServicioCorreoElectronico correo;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<string, List<string>> miembrosGrupo;
        private readonly Func<string, Task<string>> enlaceFicha;

        public AvisadorFichaProducto(IServicioCorreoElectronico correo, IServicioNotificacionesPush notificaciones)
            : this(correo, notificaciones, null, null)
        {
        }

        internal AvisadorFichaProducto(IServicioCorreoElectronico correo, IServicioNotificacionesPush notificaciones,
            Func<string, List<string>> miembrosGrupo, Func<string, Task<string>> enlaceFicha)
        {
            this.correo = correo ?? throw new ArgumentNullException(nameof(correo));
            this.notificaciones = notificaciones ?? throw new ArgumentNullException(nameof(notificaciones));
            this.miembrosGrupo = miembrosGrupo ?? MiembrosGrupoDominio.Leer;
            this.enlaceFicha = enlaceFicha ?? ProductoDTO.RutaEnlace;
        }

        internal static string EnlaceCerrar(Guid clave, string resultado) => $"{URL_API}{RUTA_ENLACE}{clave:D}?resultado={resultado}";

        public async Task AvisarEquipo(AvisoFicha aviso, DatosFichaActual ficha, IReadOnlyCollection<string> camposNuevos)
        {
            string quien = aviso.Informantes.LastOrDefault();
            string titulo = $"Ficha del {aviso.Producto}: {CamposFicha.Textos(camposNuevos)} mal (avisa {quien} desde el almacén)";
            string enlace = null;
            try
            {
                enlace = await enlaceFicha(aviso.Producto).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Sin enlace a la tienda el aviso sale igual
            }
            string enlaceOtro = null;
            if (aviso.InfoCodigo?.OtroProducto != null)
            {
                try
                {
                    enlaceOtro = await enlaceFicha(aviso.InfoCodigo.OtroProducto.Producto).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Igual que el del producto del aviso
                }
            }

            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress("nesto@nuevavision.es"),
                    Subject = titulo,
                    IsBodyHtml = true,
                    Body = CuerpoCorreo(aviso, ficha, camposNuevos, enlace, enlaceOtro)
                };
                mail.To.Add(new MailAddress(DestinosAvisoFicha.Correo(aviso.Destino)));
                _ = correo.EnviarCorreoSMTP(mail);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ariadna#8] No se ha podido mandar el correo del aviso {aviso.Id} ({aviso.Producto})", ex));
            }

            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = CuerpoTexto(aviso, ficha, camposNuevos)
                    + Environment.NewLine + "Para cerrarlo, «Cambiado» o «Estaba bien» en el correo que ha llegado a " + DestinosAvisoFicha.Correo(aviso.Destino) + ".",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["avisoId"] = aviso.Id.ToString(CultureInfo.InvariantCulture),
                    ["producto"] = aviso.Producto
                }
            };
            List<string> usuarios = (miembrosGrupo(DestinosAvisoFicha.Grupo(aviso.Destino)) ?? new List<string>())
                .Select(u => u?.Trim()).Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
        }

        public async Task AvisarMozos(AvisoFicha aviso)
        {
            string respuesta = aviso.Estado == EstadosAvisoFicha.CAMBIADO
                ? "Ya está cambiado. ¡Gracias por avisar!"
                : "Lo han revisado y estaba bien. Gracias por avisar igualmente.";
            var notificacion = new NotificacionPushDTO
            {
                Titulo = $"{aviso.Producto}: {CamposFicha.Textos(aviso.Campos)}",
                Cuerpo = $"Avisaste de que {CamposFicha.Textos(aviso.Campos)} del {aviso.Producto} estaba mal. {respuesta}",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION, ["producto"] = aviso.Producto, ["resultado"] = aviso.Estado }
            };
            foreach (string mozo in aviso.Informantes.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await notificaciones.GuardarEnBuzonDeUsuario(mozo, Constantes.Aplicaciones.ARIADNA, notificacion).ConfigureAwait(false);
            }
        }

        private static string CuerpoTexto(AvisoFicha aviso, DatosFichaActual ficha, IReadOnlyCollection<string> campos)
        {
            var texto = new StringBuilder();
            _ = texto.AppendLine($"{aviso.Producto} · {ficha?.Nombre}");
            foreach (string campo in campos)
            {
                string valor = ficha?.Valor(campo);
                _ = texto.AppendLine(valor == null ? $"Mal: {CamposFicha.Texto(campo)}" : $"Mal: {CamposFicha.Texto(campo)} (ahora: {(valor.Length == 0 ? "vacío" : valor)})");
            }
            if (!string.IsNullOrWhiteSpace(aviso.CodigoLeido))
            {
                _ = texto.AppendLine($"Código que ha enviado el mozo: {aviso.CodigoLeido}{ComoSeLeyo(aviso)}");
                string queEs = QueEsElCodigo(aviso);
                if (queEs != null)
                {
                    _ = texto.AppendLine(queEs);
                }
                if (aviso.InfoCodigo?.ConfirmadoTrasAviso == true)
                {
                    _ = texto.AppendLine(CONFIRMADO_TRAS_AVISO);
                }
            }
            if (!string.IsNullOrWhiteSpace(aviso.Comentarios))
            {
                _ = texto.AppendLine(aviso.Comentarios);
            }
            return texto.ToString().TrimEnd();
        }

        /// <summary>NestoAPI#604: « (leído con el escáner)», « (tecleado)» o nada si la app no lo dice.</summary>
        internal static string ComoSeLeyo(AvisoFicha aviso)
            => aviso.CodigoLeidoConEscaner == true ? " (leído con el escáner)" : aviso.CodigoLeidoConEscaner == false ? " (tecleado)" : string.Empty;

        /// <summary>NestoAPI#604: qué quiere decir el código que ha enviado el mozo; null si no se ha mirado.</summary>
        internal static string QueEsElCodigo(AvisoFicha aviso)
        {
            InfoCodigoLeido info = aviso.InfoCodigo;
            if (info == null || string.IsNullOrWhiteSpace(aviso.CodigoLeido))
            {
                return null;
            }
            if (info.OtroProducto != null)
            {
                return $"Ese código es del producto {info.OtroProducto.Producto} {info.OtroProducto.Nombre}: probablemente en el hueco hay ese producto, no un error de la ficha.";
            }
            if (!info.EsElDeLaFicha)
            {
                return "Ese código no está en ninguna ficha.";
            }
            if (aviso.CodigoLeidoConEscaner == true)
            {
                return "El código que ha leído el mozo con el escáner es el mismo de la ficha: el código de barras de la ficha casa con el envase; mira el comentario para saber qué está mal.";
            }
            string aMano = info.RecogidoAManoHoy && info.EmpaquetadoAManoHoy ? " Hoy se ha recogido y empaquetado a mano."
                : info.RecogidoAManoHoy ? " Hoy se ha recogido a mano."
                : info.EmpaquetadoAManoHoy ? " Hoy se ha empaquetado a mano."
                : string.Empty;
            return "El código que ha enviado el mozo es el mismo de la ficha: el envase no se ha podido leer con el escáner." + aMano
                + " Pídele una foto del código del envase o el número que lleva impreso.";
        }

        private static string CuerpoCorreo(AvisoFicha aviso, DatosFichaActual ficha, IReadOnlyCollection<string> campos, string enlaceFicha, string enlaceOtro)
        {
            string H(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
            var html = new StringBuilder();
            _ = html.Append($"<p>Desde el almacén (Ariadna), <b>{H(string.Join(", ", aviso.Informantes))}</b> avisa de que en la ficha de este producto hay algo mal:</p>");
            _ = html.Append($"<p><b>{H(ficha?.Nombre)}</b><br/>{H(aviso.Producto)} · {H(ficha?.Familia)} · {H(ficha?.Subgrupo)} · {H(ficha?.Valor(CamposFicha.TAMANO))}</p>");
            _ = html.Append("<ul>");
            foreach (string campo in campos)
            {
                string valor = ficha?.Valor(campo);
                _ = html.Append($"<li><b>{H(CamposFicha.Texto(campo))}</b>{(valor == null ? string.Empty : " — ahora: " + H(valor.Length == 0 ? "vacío" : valor))}</li>");
            }
            _ = html.Append("</ul>");
            if (!string.IsNullOrWhiteSpace(aviso.CodigoLeido))
            {
                _ = html.Append($"<p>Código que ha enviado: <b>{H(aviso.CodigoLeido)}</b>{H(ComoSeLeyo(aviso))}</p>");
                string queEs = QueEsElCodigo(aviso);
                if (queEs != null)
                {
                    _ = html.Append($"<p><b>{H(queEs)}</b>");
                    if (!string.IsNullOrWhiteSpace(enlaceOtro))
                    {
                        _ = html.Append($"<br/><a href=\"{H(enlaceOtro)}\">Ver el {H(aviso.InfoCodigo.OtroProducto.Producto)} en la tienda</a>");
                    }
                    _ = html.Append("</p>");
                }
                if (aviso.InfoCodigo?.ConfirmadoTrasAviso == true)
                {
                    _ = html.Append($"<p>{H(CONFIRMADO_TRAS_AVISO)}</p>");
                }
            }
            if (!string.IsNullOrWhiteSpace(aviso.Comentarios))
            {
                _ = html.Append($"<p>{H(aviso.Comentarios).Replace("\n", "<br/>")}</p>");
            }
            if (!string.IsNullOrWhiteSpace(aviso.UrlFoto))
            {
                _ = html.Append($"<p>La foto que ha visto:<br/><img src=\"{H(aviso.UrlFoto)}\" style=\"max-width:240px\"/></p>");
            }
            if (!string.IsNullOrWhiteSpace(enlaceFicha))
            {
                _ = html.Append($"<p><a href=\"{H(enlaceFicha)}\">Ver el producto en la tienda</a></p>");
            }
            _ = html.Append("<p>Cuando lo hayas revisado:</p><p>");
            _ = html.Append($"<a href=\"{H(EnlaceCerrar(aviso.Clave, EstadosAvisoFicha.CAMBIADO))}\" style=\"padding:8px 16px;background:#1565C0;color:#fff;text-decoration:none;border-radius:6px\">Cambiado</a>&nbsp;&nbsp;");
            _ = html.Append($"<a href=\"{H(EnlaceCerrar(aviso.Clave, EstadosAvisoFicha.ESTABA_BIEN))}\" style=\"padding:8px 16px;background:#E0E0E0;color:#000;text-decoration:none;border-radius:6px\">Estaba bien</a>");
            _ = html.Append("</p><p style=\"color:#666\">Si cambias el dato en la ficha, el aviso se cierra solo. Al cerrarlo, al mozo le llega la respuesta en Ariadna.</p>");
            return html.ToString();
        }
    }

    /// <summary>Ariadna#8: el job que cierra solos los avisos cuyo dato ya se ha cambiado en la ficha.</summary>
    public static class AvisosFichaProductoJobsService
    {
        /// <summary>Punto de entrada de Hangfire (patrón del resto de jobs).</summary>
        public static async Task RevisarCambios()
        {
            using (RepositorioAvisosFichaSql repositorio = RepositorioAvisosFichaSql.ConContextoPropio())
            {
                var servicio = new ServicioAvisosFicha(repositorio,
                    new AvisadorFichaProducto(new ServicioCorreoElectronico(), new ServicioNotificacionesPush()),
                    new FotosProductoAlmacen());
                int cerrados = await servicio.RevisarCambios().ConfigureAwait(false);
                if (cerrados > 0)
                {
                    Console.WriteLine($"[Ariadna#8] {cerrados} avisos de ficha cerrados solos (el dato ha cambiado)");
                }
            }
        }
    }
}
