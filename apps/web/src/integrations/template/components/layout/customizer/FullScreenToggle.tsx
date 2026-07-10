import { useState } from 'react'
import { LuFullscreen, LuMinimize } from 'react-icons/lu'

const FullScreenToggle = () => {
  const [fullScreenOn, setFullScreenOn] = useState(false)

  const toggleFullScreen = () => {
    const doc = document as Document & {
      mozFullScreenElement?: Element
      webkitFullscreenElement?: Element
      mozCancelFullScreen?: () => void
      webkitCancelFullScreen?: () => void
    }
    const docEl = document.documentElement as HTMLElement & {
      mozRequestFullScreen?: () => void
      webkitRequestFullscreen?: () => void
    }

    if (!doc.fullscreenElement && !doc.mozFullScreenElement && !doc.webkitFullscreenElement) {
      if (docEl.requestFullscreen) {
        docEl.requestFullscreen()
      } else if (docEl.mozRequestFullScreen) {
        docEl.mozRequestFullScreen()
      } else if (docEl.webkitRequestFullscreen) {
        docEl.webkitRequestFullscreen()
      }
      setFullScreenOn(true)
    } else {
      if (document.exitFullscreen) {
        document.exitFullscreen()
      } else if (doc.mozCancelFullScreen) {
        doc.mozCancelFullScreen()
      } else if (doc.webkitCancelFullScreen) {
        doc.webkitCancelFullScreen()
      }
      setFullScreenOn(false)
    }
  }

  return (
    <button
      className="btn size-9 rounded-full btn-sm hover:bg-default-150 group"
      id="fullscreenBtn"
      data-toggle="fullscreen"
      aria-label="Full Screen"
      onClick={toggleFullScreen}
    >
      {fullScreenOn ? <LuMinimize className="size-5" /> : <LuFullscreen className="size-5" />}
    </button>
  )
}

export default FullScreenToggle
