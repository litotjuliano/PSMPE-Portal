import product02 from '../../assets/images/product/img-02.png'
import product03 from '../../assets/images/product/img-03.png'
import product04 from '../../assets/images/product/img-04.png'
import product05 from '../../assets/images/product/img-05.png'
import product06 from '../../assets/images/product/img-06.png'

import { LuEllipsisVertical, LuShoppingCart, LuStar, LuStarHalf } from 'react-icons/lu'
import { Link } from 'react-router-dom'

type Product = {
  id: number
  name: string
  image: string
  rating: number
  reviews: number
}
const TopSellingProducts = () => {
  const products: Product[] = [
    { id: 1, name: 'Mesh Ergonomic Black Chair', image: product02, rating: 5.5, reviews: 798 },
    { id: 2, name: 'Fastcolors Typography Men', image: product03, rating: 5.5, reviews: 695 },
    { id: 3, name: 'Mesh Ergonomic Green Chair', image: product04, rating: 5.5, reviews: 985 },
    { id: 4, name: 'Techel Black Bluetooth Soundbar', image: product05, rating: 5.5, reviews: 813 },
    { id: 5, name: 'Bovet Fleurier AIFSQ029', image: product06, rating: 5.5, reviews: 915 },
    { id: 6, name: 'Fastcolors Typography Men', image: product03, rating: 5.5, reviews: 785 },
  ]
  return (
    <div className="card">
      <div className="card-header">
        <h6 className="card-title">Top Selling Products</h6>

        <div className="hs-dropdown relative inline-flex">
          <button
            type="button"
            className="hs-dropdown-toggle bt size-7.5 text-sm font-semibold rounded-lg hover:bg-default-150"
            aria-haspopup="menu"
            aria-expanded="false"
            aria-label="Dropdown"
            hs-dropdown-placement="bottom-end"
          >
            <LuEllipsisVertical className="size-4 text-default-500" />
          </button>

          <div className="hs-dropdown-menu" role="menu">
            {['1 Weekly', '1 Monthly', '3 Monthly', '6 Monthly', 'This Yearly'].map((item, index) => (
              <Link key={index} className="flex items-center gap-x-3.5 py-1.5 font-medium px-3 text-default-600 hover:bg-default-150 rounded" to="#">
                {item}
              </Link>
            ))}
          </div>
        </div>
      </div>

      <div className="card-body">
        <div className="flex flex-col gap-5">
          {products.map((product) => (
            <div key={product.id} className="flex items-center justify-between">
              <div className="flex items-center gap-3 whitespace-nowrap">
                <div className="flex items-center justify-center rounded-md size-10 bg-default-100">
                  <img src={product.image} alt={product.name} className="h-6" height={23} width={23} />
                </div>
                <div>
                  <h6 className="font-semibold text-default-800 md:w-full truncate w-43 mb-1 text-sm">{product.name}</h6>
                  <div className="flex items-center text-yellow-500 gap-0.5">
                    {Array.from({ length: Math.floor(product.rating) }).map((_, i) => (
                      <LuStar key={i} className="fill-yellow-500 size-3.5" />
                    ))}
                    {product.rating % 1 !== 0 && <LuStarHalf className="fill-yellow-500 size-3.5" />}
                  </div>
                </div>
              </div>

              <div className="flex gap-1.5 items-center">
                <LuShoppingCart className="size-4 text-default-500" />
                <h6 className="font-semibold text-default-800 text-sm">{product.reviews}</h6>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

export default TopSellingProducts
