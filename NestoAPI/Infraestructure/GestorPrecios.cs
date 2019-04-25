using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public class GestorPrecios
    {
        public static bool calcularAplicarDescuento(Producto producto)
        {
            return calcularAplicarDescuento(producto.Aplicar_Dto, producto.Familia, producto.SubGrupo);
        }
        
        public static bool calcularAplicarDescuento(bool aplicarDescuento, string familia, string subGrupo)
        {
            // Esto podríamos sacarlo a otra clase que sea más fácil de mantener
            // if (!aplicarDescuento || familia == "Ramason" || subGrupo == "ACP")
            if (!aplicarDescuento)
            {
                return false;
            } else
            {
                return true;
            }

        }

        public static void calcularDescuentoProducto(PrecioDescuentoProducto datos)
        {

            DescuentosProducto dtoProducto;

            datos.descuentoCalculado = 0;
            datos.precioCalculado = (decimal)datos.producto.PVP;

            datos.aplicarDescuento = calcularAplicarDescuento(datos.aplicarDescuento, datos.producto.Familia, datos.producto.SubGrupo);

            // En Nesto Viejo, si no tiene el aplicar descuento marcado, solo calcula precios especiales, pero no descuentos
            // Ahora hacemos que no calcule nada, por eso lo pongo aquí arriba.
            if (!datos.aplicarDescuento)
            {
                return;
            }


            using (NVEntities db = new NVEntities()) {
                // AQUÍ CALCULA PRECIOS, NO DESCUENTOS
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     ' and contacto='0  ' and [nº producto]= '29487' and empresa='1  ' AND CANTIDADMÍNIMA<=1

                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == datos.contacto && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     '  and [nº producto]= '29487' and empresa='1  ' AND CantidadMínima<=1
                //select recargopvp from clientes with (nolock) where empresa='1  ' and [nº cliente]='15191     ' and contacto='0  '
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     ' and contacto='0  ' order by cantidadminima desc
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     '  order by cantidadminima desc
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente] is null and [nºproveedor] is null order by cantidadminima desc
                dtoProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }

                // CALCULA DESCUENTOS
                // Carlos 25/04/19: si es material promocional va con el 100% de descuento
                if (datos.producto.SubGrupo == Constantes.Productos.SUBGRUPO_MUESTRAS)
                {
                    datos.descuentoCalculado = 1;
                }
                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente] is null and nºproveedor is null and familia is null
                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente] is null and  nºproveedor is null and familia is null
                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente]='15191     ' and nºproveedor is null and familia is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.Familia == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente]='15191     ' and nºproveedor is null and familia is null

                // AGAIN AND AGAIN AND AGAIN...
                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and grupoproducto='PEL' and cantidadmínima<=1 and familia is null and nºproveedor is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == null && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and nºproveedor is null and grupoproducto is null
                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and familia='Lisap     ' and cantidadmínima<=1 and nºproveedor is null  and grupoproducto is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null && d.FiltroProducto == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                // Descuento por familia con filtro y contacto
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == datos.contacto && d.Familia == datos.producto.Familia && datos.producto.Nombre.StartsWith(d.FiltroProducto) && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                // Descuento por familia con filtro sin contacto
                if (dtoProducto == null) // solo entramos si no ha encontrado el descuento con el contacto
                {
                    dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == null && d.Familia == datos.producto.Familia && datos.producto.Nombre.StartsWith(d.FiltroProducto) && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null);
                    if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                    {
                        datos.descuentoCalculado = dtoProducto.Descuento;
                    }
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and grupoproducto='PEL' and nºproveedor is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                if (datos.precioCalculado < datos.producto.PVP * (1 - datos.descuentoCalculado))
                {
                    datos.descuentoCalculado = 0;
                }
                else
                {
                    datos.precioCalculado = (decimal)datos.producto.PVP;
                }

                // Si quisiéramos comprobar también las condiciones que tiene en ficha, descomentar la siguiente línea
                // comprobarCondiciones(datos);
            }
        }
        
        private static void cargarListaCondiciones()
        {
            // Rellenamos la lista estática de las condiciones que queremos comprobar.
            // Se implenta de esta forma para que sea sencillo en el futuro poner o 
            // o quitar condiciones, ya que se implementa como clases individuales que
            // implementan el interfaz ICondicionPrecioDescuento
            listaCondiciones.Add(new OtrosAparatosNoPuedeLlevarDescuento());
            listaCondiciones.Add(new DuNoPuedeLlevarCualquierDescuento());
            listaCondiciones.Add(new RamasonNoPuedeLlevarNingunDescuento());
            listaCondiciones.Add(new CerasConPrecioMinimo());
            listaCondiciones.Add(new ThuyaNoPuedeLlevarCualquierDescuento());
        }

        private static void CargarListaValidadoresPedido()
        {
            listaValidadoresDenegacion.Add(new ValidadorOfertasYDescuentosPermitidos());
            listaValidadoresDenegacion.Add(new ValidadorOtrosAparatosSiempreSinDescuento());

            listaValidadoresAceptacion.Add(new ValidadorOfertasCombinadas());
            listaValidadoresAceptacion.Add(new ValidadorMuestrasYMaterialPromocional());
            listaValidadoresAceptacion.Add(new ValidadorRegalosTiendaOnline());
        }

        public static bool comprobarCondiciones(PrecioDescuentoProducto datos) {
            // Primero miramos si ese precio está en las oferta (tiene metidos los precios generales para todos los clientes)
            if (!datos.descuentosRellenos)
            {
                using (NVEntities db = new NVEntities())
                {
                    // Lo hacemos así para que se pueda rellenar por fuera para los test
                    datos.descuentosProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).Where(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null).ToList();
                }
            }
            if (datos.descuentosProducto != null)
            {
                DescuentosProducto dtoProducto = datos.descuentosProducto.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio * (1 - dtoProducto.Descuento) <= datos.producto.PVP * (1 - datos.descuentoReal))
                {
                    return true;
                }
            }


            // Recorre listaCondiciones y mira una a una si "datos" cumplen todas las condiciones
            // Devuelve true si cumple todas y false si alguna no se cumple
            // Las que no se cumplen son corregidas durante la comprobación
            if (listaCondiciones == null || listaCondiciones.Count == 0)
            {
                cargarListaCondiciones();
            }

            bool cumpleTodasLasCondiciones = true;

            // Cambiar el for por un while para mejorar rendimiento
            foreach (ICondicionPrecioDescuento condicion in listaCondiciones) {
                cumpleTodasLasCondiciones = condicion.precioAceptado(datos) && cumpleTodasLasCondiciones;
            };
            return cumpleTodasLasCondiciones;
        }

        public static List<ICondicionPrecioDescuento> listaCondiciones = new List<ICondicionPrecioDescuento>();

        // Dos listas de validadores:
        // - Denegación: son aquellos que buscan los pedidos que NO son válidos
        // - Aceptación: una vez un validador de denegación ha encontrado un pedido que NO es válido
        //   se pasa el validador de aceptación por si hay algún motivo para que SÍ sea válido
        public static List<IValidadorDenegacion> listaValidadoresDenegacion = new List<IValidadorDenegacion>();
        public static List<IValidadorAceptacion> listaValidadoresAceptacion = new List<IValidadorAceptacion>();


        // Es para poder sobreescribirlo en los tests
        public static IServicioPrecios servicio = new ServicioPrecios();

        // Método para validar todas las ofertas de un pedido
        public static RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido)
        {
            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = true
            };

            if (pedido.cliente == Constantes.ClientesEspeciales.EL_EDEN)
            {
                return respuesta;
            }

            if (listaValidadoresDenegacion == null || listaValidadoresDenegacion.Count == 0)
            {
                CargarListaValidadoresPedido();
            }

            foreach (IValidadorDenegacion validador in listaValidadoresDenegacion)
            {
                RespuestaValidacion respuestaValidacion = validador.EsPedidoValido(pedido, servicio);
                if (!respuestaValidacion.ValidacionSuperada || respuesta.Motivo == null)
                {
                    respuesta = respuestaValidacion;
                }
                if (!respuestaValidacion.ValidacionSuperada)
                {
                    if (!respuestaValidacion.AutorizadaDenegadaExpresamente)
                    {
                        // Si no está expresamente denegada, miramos si algún validador de aceptación lo valida
                        RespuestaValidacion respuestaAceptacion = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, respuestaValidacion.ProductoId);
                        if (respuestaAceptacion.ValidacionSuperada)
                        {
                            respuesta = respuestaAceptacion;
                            continue;
                        }
                    }
                    break;
                }
            };

            return respuesta;
        }

        public static RespuestaValidacion ComprobarValidadoresDeAceptacion(PedidoVentaDTO pedido, string numeroProducto)
        {
            if (listaValidadoresAceptacion == null || listaValidadoresAceptacion.Count == 0)
            {
                CargarListaValidadoresPedido();
            }

            RespuestaValidacion respuesta = new RespuestaValidacion
            {
                ValidacionSuperada = false,
                Motivo = "No hay ninguna oferta que permita poner el producto "+numeroProducto+" a ese precio"
            };

            foreach (IValidadorAceptacion validador in listaValidadoresAceptacion)
            {
                respuesta = validador.EsPedidoValido(pedido, numeroProducto, servicio);
                if (respuesta.ValidacionSuperada)
                {
                    break;
                }
            };

            return respuesta;
        }
    }

    public class PrecioDescuentoProducto
    {
        public decimal precioCalculado;
        public decimal descuentoCalculado;
        public Producto producto;
        public string cliente;
        public string contacto;
        public short cantidad;
        public short cantidadOferta;
        public bool aplicarDescuento;
        public string motivo; // cadena para mostrar en la interfaz de usuario
        public decimal? descuentoReal {
            get
            {
                decimal dividendo = (precioCalculado * (1 -descuentoCalculado)  * cantidad);
                decimal divisor = ((decimal)producto.PVP * (cantidad + cantidadOferta));
                return divisor != 0 ? 1 - ( dividendo / divisor ) : 0;
            }
        }
        public decimal precioCalculadoDeFicha
        {
            get
            {
                decimal? precioProducto = producto.PVP;
                decimal? descuentoFinal = 1 - descuentoReal;
                double baseRedondear = (double)(precioProducto * descuentoFinal);
                return (decimal)Math.Round(baseRedondear, 2);
            }

        }
        public bool descuentosRellenos;
        public List<DescuentosProducto> descuentosProducto;
    }

    public class RespuestaValidacion
    {
        public bool ValidacionSuperada { get; set; }
        public string Motivo { get; set; }
        public string ProductoId { get; set; }
        public bool AutorizadaDenegadaExpresamente { get; set; }
    }

    public interface ICondicionPrecioDescuento 
    {
        // Pasamos datos del producto para ver qué precio mínimo se le puede dejar.
        // En caso de ser correctos todos los datos que hemos pasado, el procedimiento
        // devolverá true sin modificar "precio", y en caso de no poder ser esas condiciones
        // devolverá false y se modificarán los campos de "precio" que sea necesario para
        // que se pueda producir la venta cumpliendo nuestras condiciones.
        bool precioAceptado(PrecioDescuentoProducto precio);
    }

    public interface IValidadorDenegacion
    {
        RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio);
    }

    public interface IValidadorAceptacion
    {
        RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, string numeroProducto, IServicioPrecios servicio);
    }

}
