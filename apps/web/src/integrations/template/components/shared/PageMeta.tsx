type PageMetaProps = {
  title: string
}

const PageMeta = ({ title }: PageMetaProps) => {
  return <title>{title ? `${title} | PSMPE Portal` : 'PSMPE Portal'}</title>
}

export default PageMeta
