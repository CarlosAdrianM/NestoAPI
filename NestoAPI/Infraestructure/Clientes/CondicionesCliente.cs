using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>PUT api/Clientes/{empresa}/{cliente}/Iva.</summary>
    public class CambioIvaClienteDTO
    {
        public string Iva { get; set; }
    }

    /// <summary>La respuesta del cambio de IVA: lo que había (por contacto) y lo que queda.</summary>
    public class ResultadoIvaClienteDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Iva { get; set; }
        /// <summary>False si todos los contactos ya lo tenían (no se escribe nada).</summary>
        public bool Cambiado { get; set; }
        /// <summary>«Contacto 0: G21 → E52».</summary>
        public List<string> Cambios { get; set; } = new List<string>();
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>POST api/Clientes/{empresa}/{cliente}/DescuentosPactados: UNA fila de DescuentosProducto del cliente.</summary>
    public class DescuentoPactadoPeticionDTO
    {
        public string Familia { get; set; }
        public string Producto { get; set; }
        public string GrupoProducto { get; set; }
        /// <summary>El nombre del producto EMPIEZA por este texto (con <see cref="Familia"/>, como en el motor de precios).</summary>
        public string FiltroProducto { get; set; }
        /// <summary>En TANTO POR UNO (0,50 = 50 %). O esto o <see cref="Precio"/>.</summary>
        public decimal? Descuento { get; set; }
        /// <summary>Precio fijo (solo con <see cref="Producto"/>). O esto o <see cref="Descuento"/>.</summary>
        public decimal? Precio { get; set; }
        /// <summary>Por defecto 1.</summary>
        public short? CantidadMinima { get; set; }
    }

    /// <summary>Una fila de DescuentosProducto de un cliente.</summary>
    public class DescuentoPactadoDTO
    {
        public int NumeroOrden { get; set; }
        public string Contacto { get; set; }
        public string Producto { get; set; }
        public string Familia { get; set; }
        public string GrupoProducto { get; set; }
        public string SubGrupoProducto { get; set; }
        public string FiltroProducto { get; set; }
        public string Proveedor { get; set; }
        public short CantidadMinima { get; set; }
        public decimal Descuento { get; set; }
        public decimal? Precio { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Campana { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>Un producto de ejemplo del ensayo, con su PVP y lo que saldría con la fila.</summary>
    public class ProductoEjemploDescuentoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public decimal? Pvp { get; set; }
        public decimal? PrecioConDescuento { get; set; }
        public decimal? DescuentoEfectivo { get; set; }
    }

    /// <summary>La respuesta del POST (ensayo o de verdad).</summary>
    public class ResultadoDescuentoPactadoDTO
    {
        public bool Ensayo { get; set; }
        /// <summary>De verdad: true si se ha creado; false si ya había una igual (con el mismo valor).</summary>
        public bool Creada { get; set; }
        /// <summary>La fila creada (o la que ya había).</summary>
        public DescuentoPactadoDTO Fila { get; set; }
        /// <summary>A cuántos productos vivos (Estado ≥ 0) aplica la fila.</summary>
        public int ProductosAfectados { get; set; }
        public ProductoEjemploDescuentoDTO Ejemplo { get; set; }
        public string Mensaje { get; set; }
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>Resultado del servicio: Estado HTTP (200 por defecto), motivo de usted y, en un 409, el código.</summary>
    public class ResultadoCondicionesCliente<T>
    {
        public int Estado { get; set; } = 200;
        public string Motivo { get; set; }
        public string Codigo { get; set; }
        public T Datos { get; set; }

        public static ResultadoCondicionesCliente<T> Error(int estado, string motivo, string codigo = null)
            => new ResultadoCondicionesCliente<T> { Estado = estado, Motivo = motivo, Codigo = codigo };

        public static ResultadoCondicionesCliente<T> Ok(T datos) => new ResultadoCondicionesCliente<T> { Datos = datos };
    }

    /// <summary>Lo que se lee de cada contacto para cambiar el IVA.</summary>
    public class FichaIvaCliente
    {
        public string Contacto { get; set; }
        public string Iva { get; set; }
    }

    /// <summary>
    /// Carlos 26/11/26 (dos casos reales en un día: el IVA del 42178 y el 50 % de Keraplant del 35253, los dos por script de sa): el IVA
    /// de una ficha y los descuentos pactados de un cliente por la API. LAS REGLAS, puras.
    /// </summary>
    public static class ReglasCondicionesCliente
    {
        public const string TABLA_IVA = "ClienteIva";
        public const string TABLA_DESCUENTO = "DescuentoPactado";
        public const string CODIGO_YA_EXISTE = "DESCUENTO_PACTADO_YA_EXISTE";

        private static readonly string[] GRUPOS_IVA = { Constantes.GruposSeguridad.ADMINISTRACION, Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA };
        private static readonly string[] GRUPOS_DESCUENTOS = { Constantes.GruposSeguridad.DIRECCION, Constantes.GruposSeguridad.INFORMATICA };

        /// <summary>El IVA de la ficha (y leer los descuentos pactados): Administración, Dirección, Informática. Nunca un cliente de la tienda.</summary>
        public static bool PuedeCambiarIva(IPrincipal usuario) => EsDeAlgunGrupo(usuario, GRUPOS_IVA);

        /// <summary>Crear o borrar un descuento pactado (son condiciones comerciales): Dirección e Informática.</summary>
        public static bool PuedeCambiarDescuentos(IPrincipal usuario) => EsDeAlgunGrupo(usuario, GRUPOS_DESCUENTOS);

        private static bool EsDeAlgunGrupo(IPrincipal usuario, string[] grupos)
        {
            ClaimsIdentity identity = usuario?.Identity as ClaimsIdentity;
            if (identity == null || identity.FindFirst("cliente") != null)
            {
                return false;
            }
            return identity.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Substring(v.LastIndexOf('\\') + 1).Trim())
                .Any(g => grupos.Contains(g, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Null si el IVA vale: tiene que ser uno de los de ParametrosIVA de la empresa. Sin IVA (la empresa espejo) no vale aquí.</summary>
        public static string MotivoIvaNoValido(string iva, IEnumerable<string> ivasValidos)
        {
            string limpio = iva?.Trim();
            List<string> validos = (ivasValidos ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrEmpty(limpio))
            {
                return "Diga el IVA que tiene que tener el cliente. Sin IVA no se puede dejar por aquí.";
            }
            return validos.Contains(limpio, StringComparer.OrdinalIgnoreCase) ? null
                : $"El IVA {limpio} no existe en los parámetros de IVA. Los que hay son: {string.Join(", ", validos)}.";
        }

        /// <summary>El IVA tal como está en ParametrosIVA (con sus mayúsculas).</summary>
        public static string IvaNormalizado(string iva, IEnumerable<string> ivasValidos)
            => (ivasValidos ?? Enumerable.Empty<string>()).Select(v => v?.Trim()).FirstOrDefault(v => string.Equals(v, iva?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? iva?.Trim();

        /// <summary>«Contacto 0: G21 → E52» de cada contacto que cambia.</summary>
        public static List<string> CambiosIva(IEnumerable<FichaIvaCliente> fichas, string iva)
            => (fichas ?? Enumerable.Empty<FichaIvaCliente>())
                .Where(f => !string.Equals(f.Iva?.Trim(), iva, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Contacto?.Trim())
                .Select(f => $"Contacto {f.Contacto?.Trim()}: {(string.IsNullOrWhiteSpace(f.Iva) ? "sin IVA" : f.Iva.Trim())} → {iva}")
                .ToList();

        /// <summary>La petición, limpia (sin espacios; vacío = null; cantidad mínima 1 si no viene).</summary>
        public static DescuentoPactadoPeticionDTO Normalizar(DescuentoPactadoPeticionDTO p)
        {
            if (p == null)
            {
                return null;
            }
            return new DescuentoPactadoPeticionDTO
            {
                Familia = Limpio(p.Familia),
                Producto = Limpio(p.Producto),
                GrupoProducto = Limpio(p.GrupoProducto),
                FiltroProducto = Limpio(p.FiltroProducto),
                Descuento = p.Descuento,
                Precio = p.Precio,
                CantidadMinima = p.CantidadMinima ?? 1
            };
        }

        /// <summary>
        /// Null si la fila vale. Solo las combinaciones que el motor de precios mira para un cliente: producto (con descuento o precio),
        /// grupo, familia, familia + grupo o familia + filtro de nombre. Descuento en tanto por uno (0 &lt; d &lt; 1) o precio, uno de los dos.
        /// </summary>
        public static string MotivoNoValido(DescuentoPactadoPeticionDTO p)
        {
            if (p == null)
            {
                return "Faltan los datos del descuento.";
            }
            if (p.Producto == null && p.Familia == null && p.GrupoProducto == null)
            {
                return "Diga a qué se aplica: un producto, una familia o un grupo.";
            }
            if (p.Producto != null && (p.Familia != null || p.GrupoProducto != null || p.FiltroProducto != null))
            {
                return "Un descuento de un producto va solo con el producto (sin familia, grupo ni filtro).";
            }
            if (p.FiltroProducto != null && p.Familia == null)
            {
                return "El filtro de nombre va con una familia (como en el motor de precios).";
            }
            if (p.FiltroProducto != null && p.GrupoProducto != null)
            {
                return "Una fila con filtro de nombre no lleva grupo: o familia + grupo, o familia + filtro.";
            }
            if (p.Descuento.HasValue == p.Precio.HasValue)
            {
                return "Diga el descuento (en tanto por uno: 0,50 = 50 %) o el precio, uno de los dos.";
            }
            if (p.Descuento.HasValue && (p.Descuento.Value <= 0 || p.Descuento.Value >= 1))
            {
                return $"El descuento va en tanto por uno, entre 0 y 1 (0,50 = 50 %): {p.Descuento.Value.ToString(CultureInfo.InvariantCulture)} no vale.";
            }
            if (p.Precio.HasValue && p.Producto == null)
            {
                return "Un precio fijo solo se pacta para un producto.";
            }
            if (p.Precio.HasValue && p.Precio.Value <= 0)
            {
                return "El precio tiene que ser mayor que cero.";
            }
            if ((p.CantidadMinima ?? 1) < 1)
            {
                return "La cantidad mínima es 1 o más.";
            }
            return null;
        }

        /// <summary>
        /// La misma clave: del cliente (sin contacto, proveedor, subgrupo ni campaña), el mismo producto, familia, grupo, filtro y cantidad mínima.
        /// </summary>
        public static DescuentoPactadoDTO Igual(IEnumerable<DescuentoPactadoDTO> filas, DescuentoPactadoPeticionDTO p)
            => (filas ?? Enumerable.Empty<DescuentoPactadoDTO>()).FirstOrDefault(f =>
                Limpio(f.Contacto) == null && Limpio(f.Proveedor) == null && Limpio(f.SubGrupoProducto) == null && Limpio(f.Campana) == null
                && Mismo(f.Producto, p.Producto) && Mismo(f.Familia, p.Familia) && Mismo(f.GrupoProducto, p.GrupoProducto)
                && Mismo(f.FiltroProducto, p.FiltroProducto) && f.CantidadMinima == (p.CantidadMinima ?? 1));

        /// <summary>Si la fila que ya hay da lo mismo que lo que se pide.</summary>
        public static bool MismoValor(DescuentoPactadoDTO fila, DescuentoPactadoPeticionDTO p)
            => fila != null && (p.Precio.HasValue
                ? fila.Precio.HasValue && fila.Precio.Value == p.Precio.Value
                : !fila.Precio.HasValue && fila.Descuento == p.Descuento.Value);

        /// <summary>Lo que saldría en el producto de ejemplo con SOLO esta fila (el motor puede encontrar otra mejor).</summary>
        public static ProductoEjemploDescuentoDTO Ejemplo(string producto, string nombre, decimal? pvp, DescuentoPactadoPeticionDTO p)
        {
            if (string.IsNullOrWhiteSpace(producto))
            {
                return null;
            }
            var ejemplo = new ProductoEjemploDescuentoDTO { Producto = producto.Trim(), Nombre = nombre?.Trim(), Pvp = pvp };
            if (pvp.HasValue && pvp.Value > 0)
            {
                ejemplo.PrecioConDescuento = p.Precio.HasValue ? p.Precio.Value : RoundingHelper.Round(pvp.Value * (1 - p.Descuento.Value), 4);
                ejemplo.DescuentoEfectivo = p.Precio.HasValue ? RoundingHelper.Round(1 - (p.Precio.Value / pvp.Value), 4) : p.Descuento;
            }
            return ejemplo;
        }

        /// <summary>«Lisap · nombre que empieza por «KERAPLANT» · 50 % desde 1 ud.».</summary>
        public static string Describir(DescuentoPactadoPeticionDTO p)
        {
            var partes = new List<string>();
            if (p.Producto != null) partes.Add($"producto {p.Producto}");
            if (p.Familia != null) partes.Add($"familia {p.Familia}");
            if (p.GrupoProducto != null) partes.Add($"grupo {p.GrupoProducto}");
            if (p.FiltroProducto != null) partes.Add($"nombre que empieza por «{p.FiltroProducto}»");
            string valor = p.Precio.HasValue
                ? $"a {p.Precio.Value.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €"
                : $"al {(p.Descuento.Value * 100).ToString("0.##", CultureInfo.GetCultureInfo("es-ES"))} %";
            short cantidad = p.CantidadMinima ?? 1;
            return $"{string.Join(", ", partes)}, {valor} desde {cantidad} {(cantidad == 1 ? "unidad" : "unidades")}";
        }

        private static bool Mismo(string a, string b) => string.Equals(Limpio(a), Limpio(b), StringComparison.OrdinalIgnoreCase);

        internal static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    /// <summary>El acceso a datos del IVA de la ficha y de los descuentos pactados (sustituible en los tests).</summary>
    public interface IRepositorioCondicionesCliente
    {
        Task<List<string>> IvasValidos(string empresa);
        Task<List<FichaIvaCliente>> LeerFichas(string empresa, string cliente);
        /// <summary>En UNA transacción: el IVA de todos los contactos y la fila de Modificaciones.</summary>
        Task CambiarIva(string empresa, string cliente, string iva, string usuario, string rastroAnterior, string rastroNuevo);
        Task<List<DescuentoPactadoDTO>> LeerDescuentos(string empresa, string cliente);
        /// <summary>Cuántos productos vivos casan con la fila y el primero (por número) como ejemplo.</summary>
        Task<(int Cuantos, string Producto, string Nombre, decimal? Pvp)> ProductosAfectados(string empresa, DescuentoPactadoPeticionDTO p);
        /// <summary>En UNA transacción: la fila (si no hay una igual) y la de Modificaciones. Devuelve su Nº de orden (null = ya había una igual).</summary>
        Task<int?> CrearDescuento(string empresa, string cliente, DescuentoPactadoPeticionDTO p, string usuario, string rastro);
        /// <summary>En UNA transacción, solo si la fila es de ese cliente: borrarla y la fila de Modificaciones. False si no estaba.</summary>
        Task<bool> BorrarDescuento(string empresa, string cliente, int numeroOrden, string usuario, string rastro);
    }

    /// <summary>
    /// Carlos 26/11/26: el IVA de la ficha (todos los contactos; publica a Odoo como cualquier cambio de ficha) y los descuentos pactados
    /// de un cliente (DescuentosProducto, una fila), sin scripts de sa. Siempre con rastro en Modificaciones.
    /// </summary>
    public class ServicioCondicionesCliente
    {
        private readonly IRepositorioCondicionesCliente repositorio;

        public ServicioCondicionesCliente(IRepositorioCondicionesCliente repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public async Task<ResultadoCondicionesCliente<ResultadoIvaClienteDTO>> CambiarIva(string empresa, string cliente, CambioIvaClienteDTO peticion,
            IPrincipal usuario, Func<string, string, Task> publicar)
        {
            if (!ReglasCondicionesCliente.PuedeCambiarIva(usuario))
            {
                return ResultadoCondicionesCliente<ResultadoIvaClienteDTO>.Error(403, "El IVA de la ficha solo lo cambian Administración, Dirección e Informática.");
            }
            string emp = Empresa(empresa);
            string cli = cliente?.Trim();
            List<string> validos = await repositorio.IvasValidos(emp).ConfigureAwait(false);
            string motivo = ReglasCondicionesCliente.MotivoIvaNoValido(peticion?.Iva, validos);
            if (motivo != null)
            {
                return ResultadoCondicionesCliente<ResultadoIvaClienteDTO>.Error(400, motivo);
            }
            string iva = ReglasCondicionesCliente.IvaNormalizado(peticion.Iva, validos);
            List<FichaIvaCliente> fichas = await repositorio.LeerFichas(emp, cli).ConfigureAwait(false);
            if (fichas == null || fichas.Count == 0)
            {
                return ResultadoCondicionesCliente<ResultadoIvaClienteDTO>.Error(404, $"No existe el cliente {cli} en la empresa {emp}.");
            }
            var respuesta = new ResultadoIvaClienteDTO { Empresa = emp, Cliente = cli, Iva = iva, Cambios = ReglasCondicionesCliente.CambiosIva(fichas, iva) };
            if (respuesta.Cambios.Count == 0)
            {
                return ResultadoCondicionesCliente<ResultadoIvaClienteDTO>.Ok(respuesta);
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario?.Identity?.Name);
            await repositorio.CambiarIva(emp, cli, iva, quien, $"{emp}|{cli}: " + string.Join(", ", fichas.OrderBy(f => f.Contacto?.Trim())
                    .Select(f => $"{f.Contacto?.Trim()}={(string.IsNullOrWhiteSpace(f.Iva) ? "-" : f.Iva.Trim())}")),
                $"{emp}|{cli}: {iva}").ConfigureAwait(false);
            respuesta.Cambiado = true;
            try
            {
                await publicar(emp, cli).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[IVA de la ficha] {emp}/{cli} cambiado a {iva}, pero no se ha podido publicar a Odoo: {ex.Message}", ex));
                respuesta.Avisos.Add("El IVA está cambiado, pero no se ha podido mandar a Odoo: se mandará con el siguiente cambio de la ficha.");
            }
            return ResultadoCondicionesCliente<ResultadoIvaClienteDTO>.Ok(respuesta);
        }

        public async Task<ResultadoCondicionesCliente<List<DescuentoPactadoDTO>>> Leer(string empresa, string cliente, IPrincipal usuario)
        {
            if (!ReglasCondicionesCliente.PuedeCambiarIva(usuario))
            {
                return ResultadoCondicionesCliente<List<DescuentoPactadoDTO>>.Error(403, "Los descuentos pactados los ven Administración, Dirección e Informática.");
            }
            string emp = Empresa(empresa);
            string cli = cliente?.Trim();
            if ((await repositorio.LeerFichas(emp, cli).ConfigureAwait(false))?.Any() != true)
            {
                return ResultadoCondicionesCliente<List<DescuentoPactadoDTO>>.Error(404, $"No existe el cliente {cli} en la empresa {emp}.");
            }
            return ResultadoCondicionesCliente<List<DescuentoPactadoDTO>>.Ok(await repositorio.LeerDescuentos(emp, cli).ConfigureAwait(false) ?? new List<DescuentoPactadoDTO>());
        }

        public async Task<ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>> Crear(string empresa, string cliente, DescuentoPactadoPeticionDTO peticion,
            bool ensayo, IPrincipal usuario)
        {
            if (!ReglasCondicionesCliente.PuedeCambiarDescuentos(usuario))
            {
                return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Error(403, "Los descuentos pactados son condiciones comerciales: solo los crean Dirección e Informática.");
            }
            DescuentoPactadoPeticionDTO p = ReglasCondicionesCliente.Normalizar(peticion);
            string motivo = ReglasCondicionesCliente.MotivoNoValido(p);
            if (motivo != null)
            {
                return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Error(400, motivo);
            }
            string emp = Empresa(empresa);
            string cli = cliente?.Trim();
            if ((await repositorio.LeerFichas(emp, cli).ConfigureAwait(false))?.Any() != true)
            {
                return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Error(404, $"No existe el cliente {cli} en la empresa {emp}.");
            }
            (int cuantos, string producto, string nombre, decimal? pvp) = await repositorio.ProductosAfectados(emp, p).ConfigureAwait(false);
            var respuesta = new ResultadoDescuentoPactadoDTO
            {
                Ensayo = ensayo,
                ProductosAfectados = cuantos,
                Ejemplo = ReglasCondicionesCliente.Ejemplo(producto, nombre, pvp, p)
            };
            string descripcion = ReglasCondicionesCliente.Describir(p);
            List<DescuentoPactadoDTO> filas = await repositorio.LeerDescuentos(emp, cli).ConfigureAwait(false);
            DescuentoPactadoDTO igual = ReglasCondicionesCliente.Igual(filas, p);
            if (igual != null && !ReglasCondicionesCliente.MismoValor(igual, p))
            {
                return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Error(409,
                    $"El cliente {cli} ya tiene un descuento pactado con esa misma clave (Nº de orden {igual.NumeroOrden}) y otro valor. " +
                    "Bórrelo primero si hay que cambiarlo.", ReglasCondicionesCliente.CODIGO_YA_EXISTE);
            }
            if (cuantos == 0)
            {
                if (ensayo)
                {
                    respuesta.Avisos.Add("No aplica a ningún producto vivo: compruebe la familia, el grupo, el producto o el filtro.");
                }
                else
                {
                    return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Error(400,
                        $"El descuento ({descripcion}) no aplica a ningún producto vivo: compruebe la familia, el grupo, el producto o el filtro.");
                }
            }
            if (igual != null)
            {
                respuesta.Fila = igual;
                respuesta.Mensaje = $"El cliente {cli} ya tiene este descuento pactado ({descripcion}; Nº de orden {igual.NumeroOrden}): no se crea otro.";
                return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Ok(respuesta);
            }
            if (ensayo)
            {
                respuesta.Mensaje = $"Se crearía el descuento pactado del cliente {cli}: {descripcion}. Aplica a {cuantos} {(cuantos == 1 ? "producto vivo" : "productos vivos")}.";
                return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Ok(respuesta);
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario?.Identity?.Name);
            string rastro = JsonConvert.SerializeObject(new { Empresa = emp, Cliente = cli, p.Producto, p.Familia, p.GrupoProducto, p.FiltroProducto, p.Descuento, p.Precio, p.CantidadMinima, ProductosAfectados = cuantos });
            int? numeroOrden = await repositorio.CrearDescuento(emp, cli, p, quien, rastro).ConfigureAwait(false);
            filas = await repositorio.LeerDescuentos(emp, cli).ConfigureAwait(false);
            respuesta.Creada = numeroOrden.HasValue;
            respuesta.Fila = numeroOrden.HasValue ? filas?.FirstOrDefault(f => f.NumeroOrden == numeroOrden.Value) : ReglasCondicionesCliente.Igual(filas, p);
            respuesta.Mensaje = numeroOrden.HasValue
                ? $"Creado el descuento pactado del cliente {cli}: {descripcion}. Aplica a {cuantos} {(cuantos == 1 ? "producto vivo" : "productos vivos")}."
                : $"El cliente {cli} ya tenía este descuento pactado: no se crea otro.";
            return ResultadoCondicionesCliente<ResultadoDescuentoPactadoDTO>.Ok(respuesta);
        }

        public async Task<ResultadoCondicionesCliente<DescuentoPactadoDTO>> Borrar(string empresa, string cliente, int numeroOrden, IPrincipal usuario)
        {
            if (!ReglasCondicionesCliente.PuedeCambiarDescuentos(usuario))
            {
                return ResultadoCondicionesCliente<DescuentoPactadoDTO>.Error(403, "Los descuentos pactados son condiciones comerciales: solo los borran Dirección e Informática.");
            }
            string emp = Empresa(empresa);
            string cli = cliente?.Trim();
            DescuentoPactadoDTO fila = (await repositorio.LeerDescuentos(emp, cli).ConfigureAwait(false))?.FirstOrDefault(f => f.NumeroOrden == numeroOrden);
            if (fila == null)
            {
                return ResultadoCondicionesCliente<DescuentoPactadoDTO>.Error(404, $"El cliente {cli} no tiene el descuento con Nº de orden {numeroOrden}.");
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario?.Identity?.Name);
            bool borrada = await repositorio.BorrarDescuento(emp, cli, numeroOrden, quien, JsonConvert.SerializeObject(fila)).ConfigureAwait(false);
            return borrada
                ? ResultadoCondicionesCliente<DescuentoPactadoDTO>.Ok(fila)
                : ResultadoCondicionesCliente<DescuentoPactadoDTO>.Error(404, $"El cliente {cli} no tiene el descuento con Nº de orden {numeroOrden}.");
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }

    /// <summary>ADO sobre NestoConnection (Clientes, ParametrosIVA, DescuentosProducto, Productos y Modificaciones: la API ya los escribía).</summary>
    public class RepositorioCondicionesClienteSql : IRepositorioCondicionesCliente
    {
        private readonly string cadenaConexion;

        public RepositorioCondicionesClienteSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        internal const string SQL_IVAS = "SELECT DISTINCT RTRIM([IVA_Cliente_Prov]) FROM ParametrosIVA WHERE Empresa = @empresa AND [IVA_Cliente_Prov] IS NOT NULL";

        internal const string SQL_FICHAS = "SELECT RTRIM(Contacto), RTRIM(IVA) FROM Clientes WHERE Empresa = @empresa AND [Nº Cliente] = @cliente";

        internal const string SQL_CAMBIAR_IVA = @"
UPDATE Clientes SET IVA = @iva, Usuario = @usuario WHERE Empresa = @empresa AND [Nº Cliente] = @cliente AND ISNULL(RTRIM(IVA), '') <> @iva;
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha) VALUES (N'" + ReglasCondicionesCliente.TABLA_IVA + @"', @anterior, @nuevo, @usuario, GETDATE());";

        internal const string SQL_DESCUENTOS = @"
SELECT [Nº Orden], RTRIM(Contacto), RTRIM([Nº Producto]), RTRIM(Familia), RTRIM(GrupoProducto), RTRIM(SubGrupoProducto), FiltroProducto, RTRIM(NºProveedor),
       CantidadMínima, Descuento, Precio, CAST(FechaDesde AS datetime), CAST(FechaHasta AS datetime), Campana, RTRIM(Usuario), [Fecha Modificación]
FROM DescuentosProducto WHERE Empresa = @empresa AND [Nº Cliente] = @cliente ORDER BY [Nº Orden]";

        /// <summary>Productos vivos que casan con la fila (familia y grupo exactos, nombre que EMPIEZA por el filtro: como el motor).</summary>
        internal const string SQL_PRODUCTOS = @"
SELECT TOP 1 COUNT(*) OVER () AS Cuantos, RTRIM(p.Número) AS Producto, RTRIM(p.Nombre) AS Nombre, p.PVP
FROM Productos p
WHERE p.Empresa = @empresa AND p.Estado >= 0
  AND (@producto IS NULL OR p.Número = @producto)
  AND (@familia IS NULL OR p.Familia = @familia)
  AND (@grupo IS NULL OR p.Grupo = @grupo)
  AND (@filtro IS NULL OR p.Nombre LIKE @filtro + '%')
ORDER BY p.Número";

        internal const string SQL_CREAR = @"
IF NOT EXISTS (SELECT 1 FROM DescuentosProducto WITH (UPDLOCK, HOLDLOCK)
               WHERE Empresa = @empresa AND [Nº Cliente] = @cliente AND Contacto IS NULL AND NºProveedor IS NULL AND SubGrupoProducto IS NULL AND Campana IS NULL
                 AND ISNULL([Nº Producto], '') = ISNULL(@producto, '') AND ISNULL(Familia, '') = ISNULL(@familia, '')
                 AND ISNULL(GrupoProducto, '') = ISNULL(@grupo, '') AND ISNULL(FiltroProducto, '') = ISNULL(@filtro, '') AND CantidadMínima = @cantidad)
BEGIN
    INSERT INTO DescuentosProducto (Empresa, [Nº Producto], GrupoProducto, [Nº Cliente], CantidadMínima, Descuento, Precio, Familia, FiltroProducto,
           AudienciaOferta, Usuario, [Fecha Modificación])
    VALUES (@empresa, @producto, @grupo, @cliente, @cantidad, @descuento, @precio, @familia, @filtro, 0, @usuario, GETDATE());
    DECLARE @orden int = SCOPE_IDENTITY();
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha)
    VALUES (N'" + ReglasCondicionesCliente.TABLA_DESCUENTO + @"', RTRIM(@empresa) + N'|' + RTRIM(@cliente) + N': sin el descuento', N'Nº Orden ' + CAST(@orden AS nvarchar(12)) + N': ' + @rastro, @usuario, GETDATE());
    SELECT @orden;
END
ELSE
    SELECT CAST(NULL AS int);";

        internal const string SQL_BORRAR = @"
DELETE FROM DescuentosProducto WHERE Empresa = @empresa AND [Nº Cliente] = @cliente AND [Nº Orden] = @orden;
IF @@ROWCOUNT = 1
BEGIN
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha)
    VALUES (N'" + ReglasCondicionesCliente.TABLA_DESCUENTO + @"', @rastro, RTRIM(@empresa) + N'|' + RTRIM(@cliente) + N': borrado el Nº Orden ' + CAST(@orden AS nvarchar(12)), @usuario, GETDATE());
    SELECT 1;
END
ELSE
    SELECT 0;";

        public async Task<List<string>> IvasValidos(string empresa)
        {
            var lista = new List<string>();
            await Leer(SQL_IVAS, c => Parametros(c, empresa, null), r => lista.Add(r.IsDBNull(0) ? null : r.GetString(0))).ConfigureAwait(false);
            return lista;
        }

        public async Task<List<FichaIvaCliente>> LeerFichas(string empresa, string cliente)
        {
            var lista = new List<FichaIvaCliente>();
            await Leer(SQL_FICHAS, c => Parametros(c, empresa, cliente), r => lista.Add(new FichaIvaCliente
            {
                Contacto = r.IsDBNull(0) ? null : r.GetString(0),
                Iva = r.IsDBNull(1) ? null : r.GetString(1)
            })).ConfigureAwait(false);
            return lista;
        }

        public Task CambiarIva(string empresa, string cliente, string iva, string usuario, string rastroAnterior, string rastroNuevo)
            => EnTransaccion(SQL_CAMBIAR_IVA, c =>
            {
                Parametros(c, empresa, cliente);
                _ = c.Parameters.Add("@iva", SqlDbType.Char, 3).Value = iva;
                _ = c.Parameters.Add("@usuario", SqlDbType.NVarChar, 30).Value = Recortar(usuario, 30);
                _ = c.Parameters.Add("@anterior", SqlDbType.NVarChar, -1).Value = rastroAnterior ?? string.Empty;
                _ = c.Parameters.Add("@nuevo", SqlDbType.NVarChar, -1).Value = rastroNuevo ?? string.Empty;
            });

        public async Task<List<DescuentoPactadoDTO>> LeerDescuentos(string empresa, string cliente)
        {
            var lista = new List<DescuentoPactadoDTO>();
            await Leer(SQL_DESCUENTOS, c => Parametros(c, empresa, cliente), r => lista.Add(new DescuentoPactadoDTO
            {
                NumeroOrden = r.GetInt32(0),
                Contacto = Texto(r, 1),
                Producto = Texto(r, 2),
                Familia = Texto(r, 3),
                GrupoProducto = Texto(r, 4),
                SubGrupoProducto = Texto(r, 5),
                FiltroProducto = Texto(r, 6)?.Trim(),
                Proveedor = Texto(r, 7),
                CantidadMinima = r.GetInt16(8),
                Descuento = r.GetDecimal(9),
                Precio = r.IsDBNull(10) ? (decimal?)null : r.GetDecimal(10),
                FechaDesde = r.IsDBNull(11) ? (DateTime?)null : r.GetDateTime(11),
                FechaHasta = r.IsDBNull(12) ? (DateTime?)null : r.GetDateTime(12),
                Campana = Texto(r, 13)?.Trim(),
                Usuario = Texto(r, 14),
                FechaModificacion = r.IsDBNull(15) ? (DateTime?)null : r.GetDateTime(15)
            })).ConfigureAwait(false);
            return lista;
        }

        public async Task<(int Cuantos, string Producto, string Nombre, decimal? Pvp)> ProductosAfectados(string empresa, DescuentoPactadoPeticionDTO p)
        {
            (int, string, string, decimal?) resultado = (0, null, null, null);
            await Leer(SQL_PRODUCTOS, c =>
            {
                Parametros(c, empresa, null);
                AnadirFila(c, p);
            }, r => resultado = (r.GetInt32(0), Texto(r, 1), Texto(r, 2), r.IsDBNull(3) ? (decimal?)null : Convert.ToDecimal(r.GetValue(3), CultureInfo.InvariantCulture))).ConfigureAwait(false);
            return resultado;
        }

        public async Task<int?> CrearDescuento(string empresa, string cliente, DescuentoPactadoPeticionDTO p, string usuario, string rastro)
        {
            object orden = await EscalarEnTransaccion(SQL_CREAR, c =>
            {
                Parametros(c, empresa, cliente);
                AnadirFila(c, p);
                _ = c.Parameters.Add("@cantidad", SqlDbType.SmallInt).Value = p.CantidadMinima ?? 1;
                _ = c.Parameters.Add("@descuento", SqlDbType.Decimal).Value = p.Descuento ?? 0m;
                _ = c.Parameters.Add("@precio", SqlDbType.Decimal).Value = (object)p.Precio ?? DBNull.Value;
                _ = c.Parameters.Add("@usuario", SqlDbType.NVarChar, 30).Value = Recortar(usuario, 30);
                _ = c.Parameters.Add("@rastro", SqlDbType.NVarChar, -1).Value = rastro ?? string.Empty;
            }).ConfigureAwait(false);
            return orden == null || orden is DBNull ? (int?)null : Convert.ToInt32(orden, CultureInfo.InvariantCulture);
        }

        public async Task<bool> BorrarDescuento(string empresa, string cliente, int numeroOrden, string usuario, string rastro)
        {
            object borrada = await EscalarEnTransaccion(SQL_BORRAR, c =>
            {
                Parametros(c, empresa, cliente);
                _ = c.Parameters.Add("@orden", SqlDbType.Int).Value = numeroOrden;
                _ = c.Parameters.Add("@usuario", SqlDbType.NVarChar, 30).Value = Recortar(usuario, 30);
                _ = c.Parameters.Add("@rastro", SqlDbType.NVarChar, -1).Value = rastro ?? string.Empty;
            }).ConfigureAwait(false);
            return borrada != null && !(borrada is DBNull) && Convert.ToInt32(borrada, CultureInfo.InvariantCulture) == 1;
        }

        private static void AnadirFila(SqlCommand c, DescuentoPactadoPeticionDTO p)
        {
            _ = c.Parameters.Add("@producto", SqlDbType.NVarChar, 15).Value = (object)p.Producto ?? DBNull.Value;
            _ = c.Parameters.Add("@familia", SqlDbType.NVarChar, 10).Value = (object)p.Familia ?? DBNull.Value;
            _ = c.Parameters.Add("@grupo", SqlDbType.NVarChar, 3).Value = (object)p.GrupoProducto ?? DBNull.Value;
            _ = c.Parameters.Add("@filtro", SqlDbType.NVarChar, 50).Value = (object)p.FiltroProducto ?? DBNull.Value;
        }

        private static void Parametros(SqlCommand c, string empresa, string cliente)
        {
            _ = c.Parameters.Add("@empresa", SqlDbType.Char, 3).Value = empresa ?? string.Empty;
            if (cliente != null)
            {
                _ = c.Parameters.Add("@cliente", SqlDbType.Char, 10).Value = cliente;
            }
        }

        private async Task Leer(string sql, Action<SqlCommand> parametros, Action<SqlDataReader> fila)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(sql, conexion))
                {
                    parametros(comando);
                    using (SqlDataReader r = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await r.ReadAsync().ConfigureAwait(false))
                        {
                            fila(r);
                        }
                    }
                }
            }
        }

        private async Task EnTransaccion(string sql, Action<SqlCommand> parametros)
            => _ = await EscalarEnTransaccion(sql, parametros).ConfigureAwait(false);

        private async Task<object> EscalarEnTransaccion(string sql, Action<SqlCommand> parametros)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                using (var comando = new SqlCommand(sql, conexion, transaccion))
                {
                    parametros(comando);
                    object resultado = await comando.ExecuteScalarAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
            }
        }

        private static string Texto(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.TrimEnd();

        private static string Recortar(string texto, int largo) => string.IsNullOrEmpty(texto) || texto.Length <= largo ? texto : texto.Substring(0, largo);
    }
}
